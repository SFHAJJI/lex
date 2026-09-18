using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>One complete disposition of a held Luxembourg body at the AKN inventory boundary.</summary>
public enum LuxembourgAknArticleInventoryDisposition
{
    [JsonStringEnumMemberName("inventoried")]
    Inventoried = 1,

    [JsonStringEnumMemberName("not_akn")]
    NotAkn = 2,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 3,

    [JsonStringEnumMemberName("xml_rejected")]
    XmlRejected = 4,
}

/// <summary>One publisher-minted top-level AKN article coordinate.</summary>
public sealed record LuxembourgAknArticleCoordinate(
    string PublisherId,
    string? PublisherWId,
    string? PublisherApplicability);

/// <summary>
/// The deterministic publisher-coordinate inventory for one retained AKN expression.
/// </summary>
/// <remarks>
/// Its semantic identity deliberately excludes the custody receipt and selected transport address.
/// Those remain provenance on the enclosing outcome. Identity derives from the expression identity,
/// stable rule-profile digest and canonical ordered article members, so transporting identical
/// publisher content in a different container cannot change what this inventory means.
/// </remarks>
public sealed class LuxembourgAknArticleInventory
{
    internal LuxembourgAknArticleInventory(
        string publisherExpressionIri,
        string ruleProfileSha256,
        IReadOnlyList<LuxembourgAknArticleCoordinate> articles,
        string? publisherWorkIri,
        string? publisherLegalResourceIri,
        string? publisherApplicabilityDate)
    {
        if ((publisherWorkIri is null) != (publisherLegalResourceIri is null) ||
            (publisherWorkIri is null) != (publisherApplicabilityDate is null))
        {
            throw new ArgumentException("Publisher expression-state coordinates must be complete or absent.");
        }

        PublisherExpressionIri = publisherExpressionIri;
        RuleProfileSha256 = ruleProfileSha256;
        Articles = Array.AsReadOnly(articles.ToArray());
        PublisherWorkIri = publisherWorkIri;
        PublisherLegalResourceIri = publisherLegalResourceIri;
        PublisherApplicabilityDate = publisherApplicabilityDate;
        IdentitySha256 = IdentityOf(this);
    }

    public string PublisherExpressionIri { get; }

    public string RuleProfileSha256 { get; }

    public IReadOnlyList<LuxembourgAknArticleCoordinate> Articles { get; }

    public string? PublisherWorkIri { get; }

    public string? PublisherLegalResourceIri { get; }

    public string? PublisherApplicabilityDate { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(LuxembourgAknArticleInventory inventory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-akn-article-inventory/2");
        Append(hash, inventory.PublisherExpressionIri);
        Append(hash, inventory.RuleProfileSha256);
        Append(hash, inventory.PublisherWorkIri ?? "");
        Append(hash, inventory.PublisherLegalResourceIri ?? "");
        Append(hash, inventory.PublisherApplicabilityDate ?? "");
        foreach (var article in inventory.Articles)
        {
            Append(hash, article.PublisherId);
            Append(hash, article.PublisherWId ?? "");
            Append(hash, article.PublisherApplicability ?? "");
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

/// <summary>One held input, its exact transport provenance, and its AKN inventory disposition.</summary>
public sealed class LuxembourgAknArticleInventoryOutcome
{
    internal LuxembourgAknArticleInventoryOutcome(
        LuxembourgHeldBodyDerivationInput input,
        LuxembourgAknArticleInventoryDisposition disposition,
        LuxembourgAknArticleInventory? inventory,
        string? detail)
    {
        Input = input;
        TransportReceipt = input.Receipt;
        Disposition = disposition;
        Inventory = inventory;
        Detail = detail;
    }

    public LuxembourgHeldBodyDerivationInput Input { get; }

    public DurableBlobWriteReceipt TransportReceipt { get; }

    public LuxembourgAknArticleInventoryDisposition Disposition { get; }

    public LuxembourgAknArticleInventory? Inventory { get; }

    public string? Detail { get; }
}

/// <summary>The exact held-body population with one ordered AKN disposition for every member.</summary>
public sealed class LuxembourgAknArticleInventoryPopulation
{
    internal LuxembourgAknArticleInventoryPopulation(
        LuxembourgHeldBodyDerivationPopulation sourcePopulation,
        IReadOnlyList<LuxembourgAknArticleInventoryOutcome> outcomes)
    {
        SourcePopulation = sourcePopulation;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        IdentitySha256 = IdentityOf(outcomes);
    }

    public LuxembourgHeldBodyDerivationPopulation SourcePopulation { get; }

    public IReadOnlyList<LuxembourgAknArticleInventoryOutcome> Outcomes { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(IReadOnlyList<LuxembourgAknArticleInventoryOutcome> outcomes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        LuxembourgAknArticleInventory.Append(
            hash, "lex-v3-luxembourg-akn-article-inventory-population/1");
        foreach (var outcome in outcomes)
        {
            LuxembourgAknArticleInventory.Append(
                hash, outcome.Input.CorpusRecord.ObjectRef.PublisherUri);
            LuxembourgAknArticleInventory.Append(
                hash, ((int)outcome.Disposition).ToString(System.Globalization.CultureInfo.InvariantCulture));
            LuxembourgAknArticleInventory.Append(hash, outcome.Inventory?.IdentitySha256 ?? "");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// Reopens every proof-bound held Luxembourg body and inventories publisher-minted AKN article
/// coordinates without extracting or interpreting legal text.
/// </summary>
public sealed class LuxembourgAknArticleInventoryProducer
{
    private const string AknNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string SclNamespace = "http://www.scl.lu";
    private const long MaximumXmlCharacters = 64L * 1024 * 1024;
    private const string RuleProfile =
        "lex-v3-luxembourg-akn-article-inventory-profile/3\n" +
        "formats=xml-akomantoso,xml\n" +
        "namespace=http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\n" +
        "articles=top-level-publisher-id-order\n" +
        "wid=verbatim-optional-distinct\n" +
        "applicability=scl-jolux-scl-name-dateApplicability-article-owned-single\n" +
        "expression-state=frbr-and-jolux-work-expression-membership-exact-date-optional\n";
    private static readonly string RuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuleProfile)));
    private readonly ICustodyStore _custodyStore;

    public LuxembourgAknArticleInventoryProducer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 => RuleDigest;

    public static byte[] CopyRuleProfileBytes() => Encoding.UTF8.GetBytes(RuleProfile);

    public async Task<LuxembourgAknArticleInventoryPopulation> RunAsync(
        LuxembourgHeldBodyDerivationPopulation population,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(population);
        var outcomes = new List<LuxembourgAknArticleInventoryOutcome>(population.Inputs.Count);
        foreach (var input in population.Inputs.OrderBy(static input => input.ObjectOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Address.UserFormatToken is not (
                    LuxembourgUserFormatToken.XmlAkomaNtoso or LuxembourgUserFormatToken.Xml))
            {
                outcomes.Add(new(input, LuxembourgAknArticleInventoryDisposition.NotAkn, null,
                    TokenName(input.Address.UserFormatToken)));
                continue;
            }

            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, input.Receipt.Reference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                outcomes.Add(new(input,
                    LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable,
                    null, exception.Message));
                continue;
            }

            if (!TryInventory(input, bytes, out var inventory, out var failure))
            {
                outcomes.Add(new(input, LuxembourgAknArticleInventoryDisposition.XmlRejected,
                    null, failure));
                continue;
            }
            outcomes.Add(new(input, LuxembourgAknArticleInventoryDisposition.Inventoried,
                inventory, null));
        }
        return new LuxembourgAknArticleInventoryPopulation(population, outcomes);
    }

    private static bool TryInventory(
        LuxembourgHeldBodyDerivationInput input,
        ReadOnlyMemory<byte> bytes,
        out LuxembourgAknArticleInventory? inventory,
        out string? failure)
    {
        inventory = null;
        failure = null;
        XDocument document;
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
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            failure = "publisher XML rejected: " + exception.GetType().Name;
            return false;
        }

        XNamespace akn = AknNamespace;
        var root = document.Root;
        if (root?.Name != akn + "akomaNtoso")
        {
            failure = "publisher XML root or namespace is not the accepted AKN namespace";
            return false;
        }

        var elements = root.Descendants(akn + "article")
            .Where(article => !article.Ancestors(akn + "article").Any())
            .ToArray();
        if (elements.Length == 0)
        {
            failure = "publisher XML contains no top-level article";
            return false;
        }

        if (!TryExpressionCoordinate(
                root,
                input,
                out var publisherWorkIri,
                out var publisherLegalResourceIri,
                out var publisherApplicabilityDate,
                out failure))
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var wids = new HashSet<string>(StringComparer.Ordinal);
        var articles = new List<LuxembourgAknArticleCoordinate>(elements.Length);
        foreach (var element in elements)
        {
            var id = (string?)element.Attribute("id");
            if (!BoundVerbatim(id))
            {
                failure = "top-level article has no bounded verbatim publisher id";
                return false;
            }
            if (!ids.Add(id!))
            {
                failure = "duplicate publisher id: " + id;
                return false;
            }

            var wid = (string?)element.Attribute("wId");
            if (wid is not null && !BoundVerbatim(wid))
            {
                failure = "top-level article has an invalid publisher wId";
                return false;
            }
            if (wid is not null && !wids.Add(wid))
            {
                failure = "duplicate publisher wId: " + wid;
                return false;
            }

            var applicabilityValues = element.Descendants(XName.Get("jolux", SclNamespace))
                .Where(value => string.Equals(
                    (string?)value.Attribute(XName.Get("name", SclNamespace)),
                    "dateApplicability", StringComparison.Ordinal))
                .Where(value => ReferenceEquals(
                    value.Ancestors(akn + "article").FirstOrDefault(), element))
                .Select(static value => value.Value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (applicabilityValues.Length > 1)
            {
                failure = "article has conflicting applicability values: " + id;
                return false;
            }
            articles.Add(new(id!, wid, applicabilityValues.SingleOrDefault()));
        }

        inventory = new LuxembourgAknArticleInventory(
            input.SelectedWemiCandidate.ExpressionIri,
            RuleDigest,
            articles,
            publisherWorkIri,
            publisherLegalResourceIri,
            publisherApplicabilityDate);
        return true;
    }

    private static bool TryExpressionCoordinate(
        XElement root,
        LuxembourgHeldBodyDerivationInput input,
        out string? publisherWorkIri,
        out string? publisherLegalResourceIri,
        out string? publisherApplicabilityDate,
        out string? failure)
    {
        publisherWorkIri = null;
        publisherLegalResourceIri = null;
        publisherApplicabilityDate = null;
        failure = null;
        XNamespace akn = AknNamespace;
        XNamespace scl = SclNamespace;
        var identification = root.Descendants(akn + "identification").ToArray();
        if (identification.Length == 0)
        {
            return true;
        }
        if (identification.Length != 1)
        {
            failure = "publisher AKN must contain exactly one identification block";
            return false;
        }

        var datedLegalResources = identification[0].Descendants(scl + "JOLUXLegalResource")
            .Select(element => (Element: element, Dates: NamedValues(element, "dateApplicability")))
            .Where(static value => value.Dates.Length != 0)
            .ToArray();
        if (datedLegalResources.Length == 0)
        {
            return true;
        }
        if (datedLegalResources.Length != 1 || datedLegalResources[0].Dates.Length != 1)
        {
            failure = "publisher AKN has conflicting expression applicability coordinates";
            return false;
        }

        var rawDate = datedLegalResources[0].Dates[0];
        if (!DateOnly.TryParseExact(
                rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate) ||
            !string.Equals(
                rawDate, parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            failure = "publisher expression applicability is not one exact civil date";
            return false;
        }

        var legalResourceIris = NamedValues(datedLegalResources[0].Element, "uriThis");
        var memberOfIris = NamedValues(datedLegalResources[0].Element, "isMemberOf");
        var frbrWorkIris = identification[0].Descendants(akn + "FRBRWork")
            .SelectMany(static element => element.Elements()
                .Where(static child => child.Name.LocalName == "FRBRthis")
                .Select(static child => ((string?)child.Attribute("value"))?.Trim()))
            .Where(static value => !string.IsNullOrEmpty(value)).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        var frbrExpressionIris = identification[0].Descendants(akn + "FRBRExpression")
            .SelectMany(static element => element.Elements()
                .Where(static child => child.Name.LocalName == "FRBRthis")
                .Select(static child => ((string?)child.Attribute("value"))?.Trim()))
            .Where(static value => !string.IsNullOrEmpty(value)).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        var joluxExpressionIris = identification[0].Descendants(scl + "JOLUXExpression")
            .SelectMany(static element => NamedValues(element, "uriThis"))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (legalResourceIris.Length != 1 || memberOfIris.Length != 1 ||
            frbrWorkIris.Length != 1 || frbrExpressionIris.Length != 1 ||
            joluxExpressionIris.Length != 1)
        {
            failure = "publisher AKN expression coordinate is incomplete or ambiguous";
            return false;
        }

        var complexWorkMatches = identification[0].Descendants(scl + "JOLUXComplexWork")
            .SelectMany(static element => NamedValues(element, "uriThis"))
            .Where(value => string.Equals(value, memberOfIris[0], StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (complexWorkMatches.Length != 1 ||
            !string.Equals(legalResourceIris[0], frbrWorkIris[0], StringComparison.Ordinal) ||
            !string.Equals(legalResourceIris[0], input.SelectedWemiCandidate.RootIri, StringComparison.Ordinal) ||
            !string.Equals(frbrExpressionIris[0], input.SelectedWemiCandidate.ExpressionIri, StringComparison.Ordinal) ||
            !string.Equals(joluxExpressionIris[0], input.SelectedWemiCandidate.ExpressionIri, StringComparison.Ordinal))
        {
            failure = "publisher AKN expression coordinate does not match the selected WEMI";
            return false;
        }

        publisherWorkIri = memberOfIris[0];
        publisherLegalResourceIri = legalResourceIris[0];
        publisherApplicabilityDate = rawDate;
        return true;
    }

    private static string[] NamedValues(XElement element, string name) =>
        element.Descendants(XName.Get("jolux", SclNamespace))
            .Where(value => string.Equals(
                (string?)value.Attribute(XName.Get("name", SclNamespace)), name,
                StringComparison.Ordinal))
            .Select(static value => value.Value.Trim())
            .Where(static value => value.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool BoundVerbatim(string? value) => value is
    {
        Length: > 0 and <= 512
    } && string.Equals(value, value.Trim(), StringComparison.Ordinal)
      && value.All(static character => character is >= '!' and <= '~');

    private static string TokenName(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
        LuxembourgUserFormatToken.Xml => "xml",
        LuxembourgUserFormatToken.PdfA => "pdfa",
        LuxembourgUserFormatToken.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };
}
