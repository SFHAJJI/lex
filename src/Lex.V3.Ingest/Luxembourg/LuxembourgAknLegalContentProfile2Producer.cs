using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// One publisher index term an article carries (<c>scl:indexterm</c>): its label, verbatim, and the
/// terms nested under it, in document order. Article metadata under content /2, never wording.
/// </summary>
public sealed class LuxembourgAknIndexTerm
{
    public LuxembourgAknIndexTerm(string label, IReadOnlyList<LuxembourgAknIndexTerm> subTerms)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("An index term has a label that is not white space only.", nameof(label));
        }

        ArgumentNullException.ThrowIfNull(subTerms);
        var terms = subTerms.ToArray();
        foreach (var term in terms)
        {
            ArgumentNullException.ThrowIfNull(term, nameof(subTerms));
        }

        Label = label;
        SubTerms = Array.AsReadOnly(terms);
    }

    /// <summary>The label's character data exactly as the publisher wrote it, white space included.</summary>
    public string Label { get; }

    /// <summary>The terms nested under this one (<c>scl:indexterm/scl:indexterms</c>), in document order.</summary>
    public IReadOnlyList<LuxembourgAknIndexTerm> SubTerms { get; }
}

/// <summary>
/// Content /2: content /1's rules over inventory /4's articles, with one change. An article's
/// publisher index terms (<c>scl:indexterms</c>, a direct child of the article) are skipped as
/// <c>scl:JOLUXWork</c> is, and kept as article metadata, never as wording.
/// </summary>
/// <remarks>
/// <para>
/// WHAT CHANGED FROM /1, which stays frozen beside this and still derives run 15's checkpoint.
/// Under /1 an article's index terms are a node outside the AKN namespace, so the article is a typed
/// gap: that is 82% of code articles. /2 reads the block, keeps its terms on
/// <see cref="LuxembourgAknLegalContentArticle.IndexTerms"/> and adds no token for them, so they
/// never reach the wording digest, <c>searchable_text</c>, <c>tokens_json</c> or a quote. Every
/// other shape stays exactly what /1 makes of it: the containers, the token kinds and every typed gap
/// are /1's, and so are their failure details.
/// </para>
/// <para>
/// WHICH ARTICLES IT READS. Only an inventory /4 population, and only its inventoried bodies, in
/// ordinal order. The disposition is tested first, so a body /4 refused for naming another state is
/// never tokenized. A body is re-read through /4's own parse (the same settings and the custody-bound
/// ceiling), document element and main body, and its articles are /4's main-body articles, so a
/// body with annex, component or quoted articles reads the same articles /4 counted rather than
/// every top-level article as /1 does.
/// </para>
/// <para>
/// THE INDEX-TERM SHAPE. The block holds <c>scl:indexterm</c> elements only. Each holds one
/// <c>scl:label</c> of character data and then at most one nested block of the same shape; no
/// element in it carries an attribute (namespace declarations aside), and only white space sits
/// between its elements. That is the shape of every one of the 582,197 terms (397,179 at the top
/// level, nested up to four levels) in the main-body articles of the 10,659 run-15 bodies /4 reads.
/// A label is kept verbatim. A term whose label is white space only and which keeps no nested term
/// is the publisher's empty template (eight articles in run 15) and keeps nothing. Any other shape
/// makes the article a typed gap, because the terms could not be kept as the publisher wrote them.
/// </para>
/// </remarks>
public sealed class LuxembourgAknLegalContentProfile2Producer
{
    private const string AknNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string SclNamespace = "http://www.scl.lu";
    private const string IndexTermsFailure = "publisher index terms are not the reviewed shape: ";

    /// <summary>
    /// The rule text. It names inventory /4's own digest, so a change to the inventory this reads
    /// changes this digest too.
    /// </summary>
    private static readonly string RuleProfile =
        "lex-v3-luxembourg-akn-legal-content-profile/2\n" +
        "source=exact-reviewed-article-inventory-and-retained-bytes\n" +
        "inventory=lex-v3-luxembourg-akn-article-inventory-profile/4;sha256=" +
        LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256 +
        ";inventoried-bodies-only;publisher_file_names_other_state-never-read\n" +
        "document=inventory-4-parse-and-ceiling;inventory-4-document-element-and-main-body\n" +
        "articles=inventory-4-main-body-articles;publisher-order;ids-equal-the-inventory\n" +
        "containers=num,heading,paragraph,alinea,content,p,ol,ul,li,b,i,sup\n" +
        "skipped-article-children=scl:JOLUXWork,scl:indexterms\n" +
        "index-terms=article-metadata-never-wording;scl:indexterms-holds-scl:indexterm-only;" +
        "scl:indexterm-holds-one-scl:label-then-at-most-one-nested-scl:indexterms;no-attributes;" +
        "white-space-only-between-elements;label-character-data-only-kept-verbatim;" +
        "white-space-only-label-without-kept-sub-terms-keeps-nothing;other-shapes-typed-gap\n" +
        "tokens=text,reference,modification-start,modification-end,note-reference-with-bound-body\n" +
        "whitespace=discard-whitespace-only-nodes-preserve-other-text-verbatim\n" +
        "modifications=paired-within-one-article\n" +
        "marker-only=paired-modification-tokens-with-typed-evidence\n" +
        "unknown=typed-gap\n";

    private static readonly string RuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuleProfile)));

    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "num", "heading", "paragraph", "alinea", "content", "p", "ol", "ul", "li",
        "b", "i", "sup",
    };

    private readonly ICustodyStore _custodyStore;

    public LuxembourgAknLegalContentProfile2Producer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 => RuleDigest;

    public static byte[] CopyRuleProfileBytes() => Encoding.UTF8.GetBytes(RuleProfile);

    /// <summary>
    /// The production door: an inventory /4 population and nothing else. A population made under any
    /// other inventory profile is refused, so content /2 never reads /3's article selection.
    /// </summary>
    public async Task<LuxembourgAknLegalContentPopulation> RunAsync(
        LuxembourgAknArticleInventoryPopulation inventoryPopulation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventoryPopulation);
        if (!string.Equals(
                inventoryPopulation.RuleProfileSha256,
                LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Content /2 reads only an inventory /4 population.", nameof(inventoryPopulation));
        }

        var outcomes = new List<LuxembourgAknLegalContentOutcome>();
        foreach (var inventoryOutcome in inventoryPopulation.Outcomes
                     .OrderBy(static outcome => outcome.Input.ObjectOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The disposition first: a body /4 refused for naming another state keeps its inventory
            // as evidence, and that evidence is never read as this state's wording.
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

            if (!LuxembourgAknArticleInventoryProducerV4.TryLoad(bytes, out var document, out var loadFailure) ||
                !LuxembourgAknArticleInventoryProducerV4.TryDocument(document!, out _, out var body, out loadFailure))
            {
                AddEveryCoordinate(inventoryPopulation, inventoryOutcome, outcomes,
                    LuxembourgAknLegalContentDisposition.XmlRejected, loadFailure);
                continue;
            }

            var articles = LuxembourgAknArticleInventoryProducerV4.MainBodyArticles(body!);
            if (!CoordinatesMatch(inventoryOutcome.Inventory, articles))
            {
                AddEveryCoordinate(inventoryPopulation, inventoryOutcome, outcomes,
                    LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch,
                    "retained publisher article coordinates differ from the reviewed inventory");
                continue;
            }

            for (var index = 0; index < articles.Count; index++)
            {
                var coordinate = inventoryOutcome.Inventory.Articles[index];
                if (!TryTokenize(articles[index], out var tokens, out var indexTerms, out var failure))
                {
                    outcomes.Add(new(inventoryPopulation, inventoryOutcome, coordinate,
                        LuxembourgAknLegalContentDisposition.UnsupportedContentShape, null, failure));
                    continue;
                }

                var article = new LuxembourgAknLegalContentArticle(
                    inventoryOutcome.Inventory.PublisherExpressionIri,
                    coordinate,
                    RuleDigest,
                    tokens!,
                    indexTerms!);
                var disposition = tokens!.All(static token => token.Kind is
                    LuxembourgAknLegalContentTokenKind.ModificationStart or
                    LuxembourgAknLegalContentTokenKind.ModificationEnd)
                    ? LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence
                    : LuxembourgAknLegalContentDisposition.Admitted;
                outcomes.Add(new(inventoryPopulation, inventoryOutcome, coordinate,
                    disposition, article, null));
            }
        }
        return new LuxembourgAknLegalContentPopulation(inventoryPopulation, outcomes, RuleDigest);
    }

    private static bool CoordinatesMatch(
        LuxembourgAknArticleInventory inventory,
        IReadOnlyList<XElement> articles) =>
        articles.Count == inventory.Articles.Count
        && articles.Select(static article => (string?)article.Attribute("id"))
            .SequenceEqual(inventory.Articles.Select(static coordinate => coordinate.PublisherId),
                StringComparer.Ordinal);

    /// <summary>
    /// /1's article reading with one change: a direct-child <c>scl:indexterms</c> is skipped as
    /// <c>scl:JOLUXWork</c> is, and its terms are collected beside the tokens.
    /// </summary>
    private static bool TryTokenize(
        XElement article,
        out IReadOnlyList<LuxembourgAknLegalContentToken>? tokens,
        out IReadOnlyList<LuxembourgAknIndexTerm>? indexTerms,
        out string? failure)
    {
        var collected = new List<LuxembourgAknLegalContentToken>();
        var terms = new List<LuxembourgAknIndexTerm>();
        var openModifications = new HashSet<string>(StringComparer.Ordinal);
        tokens = null;
        indexTerms = null;
        foreach (var node in article.Nodes())
        {
            if (node is XElement element
                && element.Name == XName.Get("JOLUXWork", SclNamespace))
            {
                continue;
            }
            if (node is XElement block
                && block.Name == XName.Get("indexterms", SclNamespace))
            {
                if (!TryIndexTerms(block, terms, out failure))
                {
                    return false;
                }
                continue;
            }
            if (!TryTokenizeNode(node, collected, openModifications, out failure))
            {
                return false;
            }
        }
        if (openModifications.Count != 0)
        {
            failure = "article ends with an unclosed modification span: "
                + string.Join(",", openModifications.Order(StringComparer.Ordinal));
            return false;
        }
        if (collected.Count == 0)
        {
            failure = "article contains no publisher legal wording or marker evidence";
            return false;
        }
        tokens = collected;
        indexTerms = terms;
        failure = null;
        return true;
    }

    /// <summary>
    /// One <c>scl:indexterms</c> block: <c>scl:indexterm</c> elements only, white space between them,
    /// no attribute. Each kept term is appended to <paramref name="terms"/> in document order.
    /// </summary>
    private static bool TryIndexTerms(
        XElement block,
        List<LuxembourgAknIndexTerm> terms,
        out string? failure)
    {
        if (HasAttribute(block))
        {
            failure = IndexTermsFailure + "an attribute on scl:indexterms";
            return false;
        }

        foreach (var node in block.Nodes())
        {
            if (node is XText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Value))
                {
                    failure = IndexTermsFailure + "character data outside a label";
                    return false;
                }
                continue;
            }
            if (node is not XElement term || term.Name != XName.Get("indexterm", SclNamespace))
            {
                failure = IndexTermsFailure + "scl:indexterms holds " + NodeName(node);
                return false;
            }
            if (!TryIndexTerm(term, out var kept, out failure))
            {
                return false;
            }
            if (kept is not null)
            {
                terms.Add(kept);
            }
        }
        failure = null;
        return true;
    }

    /// <summary>
    /// One <c>scl:indexterm</c>: one <c>scl:label</c> of character data, then at most one nested
    /// <c>scl:indexterms</c>. A white-space-only label that keeps no nested term is the publisher's
    /// empty template, and <paramref name="term"/> is then null.
    /// </summary>
    private static bool TryIndexTerm(
        XElement element,
        out LuxembourgAknIndexTerm? term,
        out string? failure)
    {
        term = null;
        if (HasAttribute(element))
        {
            failure = IndexTermsFailure + "an attribute on scl:indexterm";
            return false;
        }

        XElement? label = null;
        XElement? nested = null;
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Value))
                {
                    failure = IndexTermsFailure + "character data outside a label";
                    return false;
                }
                continue;
            }
            if (node is XElement child && child.Name == XName.Get("label", SclNamespace)
                && label is null && nested is null)
            {
                label = child;
                continue;
            }
            if (node is XElement block && block.Name == XName.Get("indexterms", SclNamespace)
                && label is not null && nested is null)
            {
                nested = block;
                continue;
            }
            failure = IndexTermsFailure + (node is XElement known &&
                (known.Name == XName.Get("label", SclNamespace) || known.Name == XName.Get("indexterms", SclNamespace))
                    ? NodeName(node) + " out of place in scl:indexterm"
                    : "scl:indexterm holds " + NodeName(node));
            return false;
        }
        if (label is null)
        {
            failure = IndexTermsFailure + "scl:indexterm has no scl:label";
            return false;
        }
        if (HasAttribute(label))
        {
            failure = IndexTermsFailure + "an attribute on scl:label";
            return false;
        }

        var characters = new StringBuilder();
        foreach (var node in label.Nodes())
        {
            if (node is not XText text)
            {
                failure = IndexTermsFailure + "scl:label holds " + NodeName(node);
                return false;
            }
            characters.Append(text.Value);
        }

        var subTerms = new List<LuxembourgAknIndexTerm>();
        if (nested is not null && !TryIndexTerms(nested, subTerms, out failure))
        {
            return false;
        }

        var value = characters.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (subTerms.Count != 0)
            {
                failure = IndexTermsFailure + "an empty scl:label over nested terms";
                return false;
            }
            failure = null;
            return true;
        }

        term = new LuxembourgAknIndexTerm(value, subTerms);
        failure = null;
        return true;
    }

    private static bool HasAttribute(XElement element) =>
        element.Attributes().Any(static attribute => !attribute.IsNamespaceDeclaration);

    private static string NodeName(XNode node) => node switch
    {
        XElement element when element.Name.NamespaceName == SclNamespace => "scl:" + element.Name.LocalName,
        XElement element when element.Name.NamespaceName == AknNamespace => "akn:" + element.Name.LocalName,
        XElement element => element.Name.LocalName + " in another namespace",
        XText => "character data",
        XProcessingInstruction => "a processing instruction",
        _ => "an unsupported node",
    };

    // The token rules below are content /1's, copied so that /1's class stays byte-identical and frozen.

    private static bool TryTokenizeNode(
        XNode node,
        List<LuxembourgAknLegalContentToken> tokens,
        HashSet<string> openModifications,
        out string? failure,
        bool allowNoteReferences = true)
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
                if (!allowNoteReferences)
                {
                    failure = "publisher note bodies cannot recursively reference notes";
                    return false;
                }
                return TryNoteReference(element, tokens, out failure);
        }
        if (!Containers.Contains(element.Name.LocalName))
        {
            failure = "unsupported legal-content element: " + element.Name.LocalName;
            return false;
        }
        foreach (var child in element.Nodes())
        {
            if (!TryTokenizeNode(
                    child, tokens, openModifications, out failure, allowNoteReferences))
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
        var noteId = target[1..];
        var matches = element.Document?.Root?.Descendants()
            .Where(candidate => candidate.Name.NamespaceName == AknNamespace
                && candidate.Name.LocalName == "note"
                && string.Equals((string?)candidate.Attribute("id"), noteId, StringComparison.Ordinal))
            .Take(2).ToArray() ?? [];
        if (matches.Length != 1 ||
            !string.Equals((string?)matches[0].Attribute("marker"), marker, StringComparison.Ordinal))
        {
            failure = "note reference does not bind exactly one publisher note with the same marker";
            return false;
        }
        var noteTokens = new List<LuxembourgAknLegalContentToken>();
        var noteModifications = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in matches[0].Nodes())
        {
            if (!TryTokenizeNode(
                    child, noteTokens, noteModifications, out failure,
                    allowNoteReferences: false)) return false;
        }
        if (noteModifications.Count != 0 ||
            !noteTokens.Any(static token => token.Kind is
                LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference))
        {
            failure = "publisher note has no preserved wording or has an unclosed modification span";
            return false;
        }
        tokens.Add(new(
            LuxembourgAknLegalContentTokenKind.NoteReference,
            null,
            target,
            marker,
            Array.AsReadOnly(noteTokens.ToArray())));
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
