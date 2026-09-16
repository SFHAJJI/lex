using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>One proof-bound article's legal-content disposition.</summary>
public enum LuxembourgAknLegalContentDisposition
{
    [JsonStringEnumMemberName("admitted")]
    Admitted = 1,

    [JsonStringEnumMemberName("upstream_not_inventoried")]
    UpstreamNotInventoried = 2,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 3,

    [JsonStringEnumMemberName("xml_rejected")]
    XmlRejected = 4,

    [JsonStringEnumMemberName("article_coordinates_mismatch")]
    ArticleCoordinatesMismatch = 5,

    [JsonStringEnumMemberName("unsupported_content_shape")]
    UnsupportedContentShape = 6,
}

/// <summary>The evidence-preserving token kinds admitted by the reviewed AKN profile.</summary>
public enum LuxembourgAknLegalContentTokenKind
{
    [JsonStringEnumMemberName("text")]
    Text = 1,

    [JsonStringEnumMemberName("reference")]
    Reference = 2,

    [JsonStringEnumMemberName("modification_start")]
    ModificationStart = 3,

    [JsonStringEnumMemberName("modification_end")]
    ModificationEnd = 4,

    [JsonStringEnumMemberName("note_reference")]
    NoteReference = 5,
}

/// <summary>One ordered publisher token. Unused fields are null rather than inferred.</summary>
public sealed record LuxembourgAknLegalContentToken(
    LuxembourgAknLegalContentTokenKind Kind,
    string? Text,
    string? Target,
    string? Marker);

/// <summary>Legal content derived from one exact publisher article.</summary>
public sealed class LuxembourgAknLegalContentArticle
{
    internal LuxembourgAknLegalContentArticle(
        string publisherExpressionIri,
        LuxembourgAknArticleCoordinate coordinate,
        string ruleProfileSha256,
        IReadOnlyList<LuxembourgAknLegalContentToken> tokens)
    {
        PublisherExpressionIri = publisherExpressionIri;
        Coordinate = coordinate;
        RuleProfileSha256 = ruleProfileSha256;
        Tokens = Array.AsReadOnly(tokens.ToArray());
        IdentitySha256 = IdentityOf(this);
    }

    public string PublisherExpressionIri { get; }

    public LuxembourgAknArticleCoordinate Coordinate { get; }

    public string RuleProfileSha256 { get; }

    public IReadOnlyList<LuxembourgAknLegalContentToken> Tokens { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(LuxembourgAknLegalContentArticle article)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-akn-legal-content-article/1");
        Append(hash, article.PublisherExpressionIri);
        Append(hash, article.Coordinate.PublisherId);
        Append(hash, article.Coordinate.PublisherWId ?? "");
        Append(hash, article.Coordinate.PublisherApplicability ?? "");
        Append(hash, article.RuleProfileSha256);
        foreach (var token in article.Tokens)
        {
            Append(hash, ((int)token.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, token.Text ?? "");
            Append(hash, token.Target ?? "");
            Append(hash, token.Marker ?? "");
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

/// <summary>One article or upstream body outcome with its exact inventory and custody lineage.</summary>
public sealed class LuxembourgAknLegalContentOutcome
{
    internal LuxembourgAknLegalContentOutcome(
        LuxembourgAknArticleInventoryPopulation sourceInventoryPopulation,
        LuxembourgAknArticleInventoryOutcome sourceInventoryOutcome,
        LuxembourgAknArticleCoordinate? coordinate,
        LuxembourgAknLegalContentDisposition disposition,
        LuxembourgAknLegalContentArticle? article,
        string? detail)
    {
        SourceInventoryPopulation = sourceInventoryPopulation;
        SourceInventoryOutcome = sourceInventoryOutcome;
        Coordinate = coordinate;
        TransportReceipt = sourceInventoryOutcome.TransportReceipt;
        Disposition = disposition;
        Article = article;
        Detail = detail;
    }

    public LuxembourgAknArticleInventoryPopulation SourceInventoryPopulation { get; }

    public LuxembourgAknArticleInventoryOutcome SourceInventoryOutcome { get; }

    public LuxembourgAknArticleCoordinate? Coordinate { get; }

    public DurableBlobWriteReceipt TransportReceipt { get; }

    public LuxembourgAknLegalContentDisposition Disposition { get; }

    public LuxembourgAknLegalContentArticle? Article { get; }

    public string? Detail { get; }
}

/// <summary>The ordered, complete legal-content disposition population.</summary>
public sealed class LuxembourgAknLegalContentPopulation
{
    internal LuxembourgAknLegalContentPopulation(
        LuxembourgAknArticleInventoryPopulation sourceInventoryPopulation,
        IReadOnlyList<LuxembourgAknLegalContentOutcome> outcomes)
    {
        SourceInventoryPopulation = sourceInventoryPopulation;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        IdentitySha256 = IdentityOf(outcomes);
    }

    public LuxembourgAknArticleInventoryPopulation SourceInventoryPopulation { get; }

    public IReadOnlyList<LuxembourgAknLegalContentOutcome> Outcomes { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(IReadOnlyList<LuxembourgAknLegalContentOutcome> outcomes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        LuxembourgAknLegalContentArticle.Append(
            hash, "lex-v3-luxembourg-akn-legal-content-population/1");
        LuxembourgAknLegalContentArticle.Append(
            hash, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);
        foreach (var outcome in outcomes)
        {
            LuxembourgAknLegalContentArticle.Append(
                hash, outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri);
            LuxembourgAknLegalContentArticle.Append(hash, outcome.Coordinate?.PublisherId ?? "");
            LuxembourgAknLegalContentArticle.Append(
                hash, ((int)outcome.Disposition).ToString(System.Globalization.CultureInfo.InvariantCulture));
            LuxembourgAknLegalContentArticle.Append(hash, outcome.Article?.IdentitySha256 ?? "");
            LuxembourgAknLegalContentArticle.Append(hash, SemanticDetail(outcome));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string SemanticDetail(LuxembourgAknLegalContentOutcome outcome) =>
        outcome.Disposition is LuxembourgAknLegalContentDisposition.UpstreamNotInventoried
            or LuxembourgAknLegalContentDisposition.XmlRejected
            or LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch
            or LuxembourgAknLegalContentDisposition.UnsupportedContentShape
            ? outcome.Detail ?? ""
            : "";
}

/// <summary>
/// Reopens the exact bytes behind the reviewed AKN inventory and preserves legal wording plus
/// inline publisher evidence as an ordered token stream. It applies no layout or marker inference.
/// </summary>
public sealed class LuxembourgAknLegalContentProfileProducer
{
    private const string AknNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string SclNamespace = "http://www.scl.lu";
    private const long MaximumXmlCharacters = 64L * 1024 * 1024;
    private const string RuleProfile =
        "lex-v3-luxembourg-akn-legal-content-profile/1\n" +
        "source=exact-reviewed-article-inventory-and-retained-bytes\n" +
        "containers=num,heading,paragraph,alinea,content,p,ol,ul,li,b,i,sup\n" +
        "tokens=text,reference,modification-start,modification-end,note-reference\n" +
        "whitespace=discard-whitespace-only-nodes-preserve-other-text-verbatim\n" +
        "modifications=paired-within-one-article\n" +
        "unknown=typed-gap\n";
    private static readonly string RuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuleProfile)));
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "num", "heading", "paragraph", "alinea", "content", "p", "ol", "ul", "li",
        "b", "i", "sup",
    };
    private readonly ICustodyStore _custodyStore;

    public LuxembourgAknLegalContentProfileProducer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 => RuleDigest;

    public static byte[] CopyRuleProfileBytes() => Encoding.UTF8.GetBytes(RuleProfile);

    public async Task<LuxembourgAknLegalContentPopulation> RunAsync(
        LuxembourgAknArticleInventoryPopulation inventoryPopulation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventoryPopulation);
        var outcomes = new List<LuxembourgAknLegalContentOutcome>();
        foreach (var inventoryOutcome in inventoryPopulation.Outcomes
                     .OrderBy(static outcome => outcome.Input.ObjectOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inventoryOutcome.Disposition != LuxembourgAknArticleInventoryDisposition.Inventoried
                || inventoryOutcome.Inventory is null)
            {
                outcomes.Add(new(inventoryPopulation, inventoryOutcome, null,
                    LuxembourgAknLegalContentDisposition.UpstreamNotInventoried, null,
                    inventoryOutcome.Disposition.ToString()));
                continue;
            }

            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, inventoryOutcome.TransportReceipt.Reference, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                AddEveryCoordinate(inventoryPopulation, inventoryOutcome, outcomes,
                    LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable,
                    exception.Message);
                continue;
            }

            if (!TryLoad(bytes, out var articles, out var loadFailure))
            {
                AddEveryCoordinate(inventoryPopulation, inventoryOutcome, outcomes,
                    LuxembourgAknLegalContentDisposition.XmlRejected, loadFailure);
                continue;
            }

            if (!CoordinatesMatch(inventoryOutcome.Inventory, articles!))
            {
                AddEveryCoordinate(inventoryPopulation, inventoryOutcome, outcomes,
                    LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch,
                    "retained publisher article coordinates differ from the reviewed inventory");
                continue;
            }

            for (var index = 0; index < articles!.Length; index++)
            {
                var coordinate = inventoryOutcome.Inventory.Articles[index];
                if (!TryTokenize(articles[index], out var tokens, out var failure))
                {
                    outcomes.Add(new(inventoryPopulation, inventoryOutcome, coordinate,
                        LuxembourgAknLegalContentDisposition.UnsupportedContentShape, null, failure));
                    continue;
                }

                var article = new LuxembourgAknLegalContentArticle(
                    inventoryOutcome.Inventory.PublisherExpressionIri,
                    coordinate,
                    RuleDigest,
                    tokens!);
                outcomes.Add(new(inventoryPopulation, inventoryOutcome, coordinate,
                    LuxembourgAknLegalContentDisposition.Admitted, article, null));
            }
        }
        return new LuxembourgAknLegalContentPopulation(inventoryPopulation, outcomes);
    }

    private static bool TryLoad(
        ReadOnlyMemory<byte> bytes,
        out XElement[]? articles,
        out string? failure)
    {
        articles = null;
        failure = null;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaximumXmlCharacters,
                IgnoreComments = true,
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            XNamespace akn = AknNamespace;
            if (document.Root?.Name != akn + "akomaNtoso")
            {
                failure = "publisher XML root or namespace is not the accepted AKN namespace";
                return false;
            }
            articles = document.Root.Descendants(akn + "article")
                .Where(article => !article.Ancestors(akn + "article").Any())
                .ToArray();
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            failure = "publisher XML rejected: " + exception.GetType().Name;
            return false;
        }
    }

    private static bool CoordinatesMatch(
        LuxembourgAknArticleInventory inventory,
        IReadOnlyList<XElement> articles) =>
        articles.Count == inventory.Articles.Count
        && articles.Select(static article => (string?)article.Attribute("id"))
            .SequenceEqual(inventory.Articles.Select(static coordinate => coordinate.PublisherId),
                StringComparer.Ordinal);

    private static bool TryTokenize(
        XElement article,
        out IReadOnlyList<LuxembourgAknLegalContentToken>? tokens,
        out string? failure)
    {
        var collected = new List<LuxembourgAknLegalContentToken>();
        var openModifications = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in article.Nodes())
        {
            if (node is XElement element
                && element.Name == XName.Get("JOLUXWork", SclNamespace))
            {
                continue;
            }
            if (!TryTokenizeNode(node, collected, openModifications, out failure))
            {
                tokens = null;
                return false;
            }
        }
        if (openModifications.Count != 0)
        {
            tokens = null;
            failure = "article ends with an unclosed modification span: "
                + string.Join(",", openModifications.Order(StringComparer.Ordinal));
            return false;
        }
        if (!collected.Any(static token => token.Kind is
                LuxembourgAknLegalContentTokenKind.Text
                or LuxembourgAknLegalContentTokenKind.Reference))
        {
            tokens = null;
            failure = "article contains no publisher legal wording";
            return false;
        }
        tokens = collected;
        failure = null;
        return true;
    }

    private static bool TryTokenizeNode(
        XNode node,
        List<LuxembourgAknLegalContentToken> tokens,
        HashSet<string> openModifications,
        out string? failure)
    {
        failure = null;
        if (node is XText text)
        {
            if (!string.IsNullOrWhiteSpace(text.Value))
            {
                tokens.Add(new(LuxembourgAknLegalContentTokenKind.Text, text.Value, null, null));
            }
            return true;
        }
        if (node is not XElement element || element.Name.NamespaceName != AknNamespace)
        {
            failure = "unsupported legal-content node or namespace";
            return false;
        }

        switch (element.Name.LocalName)
        {
            case "ref":
                return TryReference(element, tokens, out failure);
            case "mod":
                return TryModification(element, tokens, openModifications, out failure);
            case "noteRef":
                return TryNoteReference(element, tokens, out failure);
        }
        if (!Containers.Contains(element.Name.LocalName))
        {
            failure = "unsupported legal-content element: " + element.Name.LocalName;
            return false;
        }
        foreach (var child in element.Nodes())
        {
            if (!TryTokenizeNode(child, tokens, openModifications, out failure))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryReference(
        XElement element,
        List<LuxembourgAknLegalContentToken> tokens,
        out string? failure)
    {
        var target = (string?)element.Attribute("href");
        var label = string.Concat(element.DescendantNodes().OfType<XText>().Select(static text => text.Value));
        if (!Bound(target) || string.IsNullOrWhiteSpace(label))
        {
            failure = "reference lacks a bounded target or publisher label";
            return false;
        }
        if (element.Descendants().Any(descendant => descendant.Name.NamespaceName != AknNamespace
            || descendant.Name.LocalName is not ("b" or "i" or "sup")))
        {
            failure = "reference contains an unsupported publisher label shape";
            return false;
        }
        tokens.Add(new(LuxembourgAknLegalContentTokenKind.Reference, label, target, null));
        failure = null;
        return true;
    }

    private static bool TryModification(
        XElement element,
        List<LuxembourgAknLegalContentToken> tokens,
        HashSet<string> openModifications,
        out string? failure)
    {
        var kind = (string?)element.Attribute("class");
        var target = (string?)element.Attribute("for");
        if (element.Nodes().Any() || !Bound(target) || !target!.StartsWith('#'))
        {
            failure = "modification boundary is not empty or lacks a bounded publisher target";
            return false;
        }
        if (kind == "mod-start")
        {
            if (!openModifications.Add(target))
            {
                failure = "duplicate open modification span: " + target;
                return false;
            }
            tokens.Add(new(LuxembourgAknLegalContentTokenKind.ModificationStart, null, target, null));
            failure = null;
            return true;
        }
        if (kind == "mod-end")
        {
            if (!openModifications.Remove(target))
            {
                failure = "modification end has no start in this article: " + target;
                return false;
            }
            tokens.Add(new(LuxembourgAknLegalContentTokenKind.ModificationEnd, null, target, null));
            failure = null;
            return true;
        }
        failure = "unsupported modification class: " + kind;
        return false;
    }

    private static bool TryNoteReference(
        XElement element,
        List<LuxembourgAknLegalContentToken> tokens,
        out string? failure)
    {
        var target = (string?)element.Attribute("href");
        var marker = (string?)element.Attribute("marker");
        if (element.Nodes().Any() || !Bound(target) || !Bound(marker) || !target!.StartsWith('#'))
        {
            failure = "note reference is not empty or lacks bounded publisher evidence";
            return false;
        }
        tokens.Add(new(LuxembourgAknLegalContentTokenKind.NoteReference, null, target, marker));
        failure = null;
        return true;
    }

    private static void AddEveryCoordinate(
        LuxembourgAknArticleInventoryPopulation inventoryPopulation,
        LuxembourgAknArticleInventoryOutcome inventoryOutcome,
        List<LuxembourgAknLegalContentOutcome> outcomes,
        LuxembourgAknLegalContentDisposition disposition,
        string? detail)
    {
        foreach (var coordinate in inventoryOutcome.Inventory!.Articles)
        {
            outcomes.Add(new(inventoryPopulation, inventoryOutcome, coordinate,
                disposition, null, detail));
        }
    }

    private static bool Bound(string? value) => value is { Length: > 0 and <= 2048 }
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
