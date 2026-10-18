using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Content /3: content /2's article reading (inventory /4's articles, the index terms kept as metadata) with the shapes the
/// Luxembourg data design's §1.4 table admits: quoted wording between quotation tokens, deleted wording as
/// <see cref="LuxembourgAknLegalContentTokenKind.DeletedText"/>, the layout markers, the inline containers, the underline
/// outside amending structures, and note references the publisher wrote without a marker.
/// </summary>
/// <remarks>
/// <para>
/// WHAT CHANGED FROM /2, which stays beside this with its own digest (as /1 stays beside both). Every shape /2 admits reads
/// here to the same tokens, and so to the same stored stream, searchable text and wording digest; its articles carry
/// identity layout <c>/3</c> under this digest. The shapes it adds:
/// </para>
/// <list type="bullet">
/// <item><c>eop</c>, <c>eol</c> and <c>br</c> are layout markers and make no token. Where one sits between two characters of
/// wording that are not white space, one text token of a single space (U+0020) stands at the marker, so two words never fuse
/// in the searchable text; inside deleted wording that space is deleted wording too.</item>
/// <item><c>sub</c>, <c>def</c>, <c>abbr</c>, <c>em</c>, <c>term</c>, <c>inline</c> and a <c>span</c> without a class are
/// containers, as <c>b</c> and <c>i</c> are.</item>
/// <item><c>u</c> outside quoted and amending structures is a container whose text, reference and deleted tokens carry
/// <see cref="LuxembourgAknLegalContentToken.Underline"/>. A <c>u</c> inside <c>quotedStructure</c>,
/// <c>embeddedStructure</c>, <c>quotedText</c>, <c>embeddedText</c> or <c>mod</c> is a typed gap: it can mark inserted
/// wording.</item>
/// <item><c>embeddedText</c>, <c>quotedText</c>, <c>embeddedStructure</c> and <c>quotedStructure</c> become a
/// <see cref="LuxembourgAknLegalContentTokenKind.QuotationStart"/> and a <see cref="LuxembourgAknLegalContentTokenKind.QuotationEnd"/>
/// around their tokens, each naming the element and whether the publisher writes the marks in the wording
/// (<c>scl:hide-quote="true"</c>), with its <c>startQuote</c> or <c>endQuote</c> verbatim as the marker. The wording between
/// them is the amending act's own: text and references, searchable, quotable and cited. The hierarchy (<c>book</c> to
/// <c>alinea</c>) is a container only inside a quotation.</item>
/// <item><c>del</c>, and a <c>span</c> of class <c>strike</c>, are deleted wording: each text inside is a
/// <see cref="LuxembourgAknLegalContentTokenKind.DeletedText"/> token, and a reference inside is one too, keeping its target,
/// so it is never a reference and emits no <c>cites</c> row. Inline containers, layout markers, nested deleted wording,
/// empty modification boundaries and note references may sit inside; anything else is a typed gap.</item>
/// <item>A <c>span</c> of any other class is a typed gap until the class is reviewed.</item>
/// <item>An element whose attribute marks a deletion (<c>style="del"</c>, or <c>class="strike"</c> on anything but a
/// <c>span</c>; the article itself included) is a typed gap unless all its wording is deleted wording (the review of #981,
/// F2): the design's /3 table reads deletion from <c>del</c> and a struck span only, so wording an attribute marks deleted
/// is never read as wording in force.</item>
/// <item>A <c>noteRef</c> with no <c>marker</c>, empty and bound by its id to exactly one note, is a note reference with
/// <see cref="LuxembourgAknLegalContentToken.Unmarked"/> and no marker. A note body holds no quotation and no deleted
/// wording, so a note's text is always wording in force.</item>
/// </list>
/// <para>
/// STILL TYPED GAPS (<c>unsupported_content_shape</c>): tables, a modification span that crosses an article and a
/// <c>mod</c> that is not empty, MathML and every other namespace, images, processing instructions, and every element this
/// profile does not name (<c>docNumber</c> among them).
/// </para>
/// </remarks>
public sealed class LuxembourgAknLegalContentProfile3Producer
{
    private const string AknNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string SclNamespace = "http://www.scl.lu";
    private const string Namespace0 = "unsupported legal-content node or namespace";
    private const string InsideDeleted = "unsupported content inside deleted wording: ";
    private const string InsideNote = "publisher note bodies hold no quotation or deleted wording: ";
    private const string AttributeDeletion = "deletion marked by an attribute holds wording not marked deleted: ";

    /// <summary>
    /// The rule text. It names inventory /4's digest and content /2's, whose index-term rule it applies, so a change to
    /// either changes this digest too.
    /// </summary>
    private static readonly string RuleProfile =
        "lex-v3-luxembourg-akn-legal-content-profile/3\n" +
        "source=exact-reviewed-article-inventory-and-retained-bytes\n" +
        "inventory=lex-v3-luxembourg-akn-article-inventory-profile/4;sha256=" +
        LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256 +
        ";inventoried-bodies-only;publisher_file_names_other_state-never-read\n" +
        "document=inventory-4-parse-and-ceiling;inventory-4-document-element-and-main-body\n" +
        "articles=inventory-4-main-body-articles;publisher-order;ids-equal-the-inventory\n" +
        "skipped-article-children=scl:JOLUXWork,scl:indexterms\n" +
        "index-terms=lex-v3-luxembourg-akn-legal-content-profile/2;sha256=" +
        LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256 + ";its-index-term-rule\n" +
        "containers=num,heading,paragraph,alinea,content,p,ol,ul,li,b,i,sup,sub,def,abbr,em,term,inline,span-without-class\n" +
        "tokens=text,reference,modification-start,modification-end,note-reference-with-bound-body," +
        "quotation-start,quotation-end,deleted-text\n" +
        "whitespace=discard-whitespace-only-nodes-preserve-other-text-verbatim\n" +
        "layout=eop,eol,br;empty;no-token;" +
        "between-two-non-white-space-characters-of-wording-one-text-token-of-one-U+0020-at-the-marker;" +
        "deleted-text-inside-deleted-wording\n" +
        "underline=u-outside-quotations-and-mod-a-container-whose-text-reference-and-deleted-tokens-carry-underline;" +
        "u-inside-embeddedStructure,embeddedText,quotedStructure,quotedText,mod-typed-gap\n" +
        "quotations=embeddedText,quotedText,embeddedStructure,quotedStructure;" +
        "quotation-start-and-quotation-end-around-their-tokens-naming-the-element;" +
        "startQuote-endQuote-verbatim-as-marker-1-to-64-characters;" +
        "scl:hide-quote-true-marks-in-wording;false-or-absent-not;other-values-typed-gap;" +
        "the-wording-inside-is-the-act's-own-text-and-references-searchable-quotable-cited\n" +
        "quoted-hierarchy=book,part,subpart,title,chapter,subchapter,section,subsection,article,paragraph,subparagraph,alinea;" +
        "containers-only-inside-a-quotation\n" +
        "deleted=del-without-class,span-class-strike;each-text-a-deleted-text-token;" +
        "a-reference-inside-a-deleted-text-token-keeping-its-target-never-a-reference;" +
        "inside:text,b,i,sup,sub,def,abbr,em,term,inline,u,span-without-class,del,span-class-strike,eop,eol,br," +
        "empty-modification-boundaries,note-references;other-content-typed-gap;del-with-a-class-typed-gap;" +
        "never-searchable-text-quoted-text-or-cites\n" +
        "spans=without-class-container;class-strike-deleted;any-other-class-typed-gap\n" +
        "attribute-marked-deletion=style-del-or-class-strike-on-an-element-other-than-del-or-span;" +
        "a-typed-gap-unless-all-its-wording-is-deleted-wording\n" +
        "references=bounded-href;label-of-text,b,i,sup\n" +
        "note-references=empty;bounded-#-target;" +
        "marker-binds-exactly-one-note-with-the-same-marker;" +
        "no-marker-binds-exactly-one-note-by-id-and-is-flagged-unmarked;" +
        "note-bodies-hold-no-quotation-and-no-deleted-wording;no-note-reference-in-a-note-body\n" +
        "modifications=paired-within-one-article;empty-boundaries-only\n" +
        "marker-only=paired-modification-tokens-with-typed-evidence\n" +
        "unknown=typed-gap\n";

    private static readonly string RuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuleProfile)));

    /// <summary>The containers outside a quotation and outside deleted wording: /1's and the inline ones.</summary>
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "num", "heading", "paragraph", "alinea", "content", "p", "ol", "ul", "li",
        "b", "i", "sup", "sub", "def", "abbr", "em", "term", "inline",
    };

    /// <summary>The containers deleted wording may hold: the inline ones (and <c>u</c> and <c>span</c>, read apart).</summary>
    private static readonly HashSet<string> InlineContainers = new(StringComparer.Ordinal)
    {
        "b", "i", "sup", "sub", "def", "abbr", "em", "term", "inline",
    };

    /// <summary>The hierarchy that is a container only inside a quotation.</summary>
    private static readonly HashSet<string> QuotedHierarchy = new(StringComparer.Ordinal)
    {
        "book", "part", "subpart", "title", "chapter", "subchapter", "section", "subsection",
        "article", "paragraph", "subparagraph", "alinea",
    };

    private static readonly HashSet<string> Quotations = new(StringComparer.Ordinal)
    {
        "embeddedText", "quotedText", "embeddedStructure", "quotedStructure",
    };

    private static readonly HashSet<string> LayoutMarkers = new(StringComparer.Ordinal) { "eop", "eol", "br" };

    private readonly ICustodyStore _custodyStore;

    public LuxembourgAknLegalContentProfile3Producer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 => RuleDigest;

    public static byte[] CopyRuleProfileBytes() => Encoding.UTF8.GetBytes(RuleProfile);

    /// <summary>
    /// The production door: an inventory /4 population and nothing else, as for content /2.
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
                "Content /3 reads only an inventory /4 population.", nameof(inventoryPopulation));
        }

        var outcomes = new List<LuxembourgAknLegalContentOutcome>();
        foreach (var inventoryOutcome in inventoryPopulation.Outcomes
                     .OrderBy(static outcome => outcome.Input.ObjectOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The disposition first: a body /4 refused for naming another state is never read as this state's wording.
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

            var notes = new NoteIndex(document!);
            for (var index = 0; index < articles.Count; index++)
            {
                var coordinate = inventoryOutcome.Inventory.Articles[index];
                if (!TryTokenize(articles[index], notes, out var tokens, out var indexTerms, out var failure))
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
                    indexTerms!,
                    content3: true);
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
    /// /2's article reading: a direct-child <c>scl:JOLUXWork</c> is skipped, a direct-child <c>scl:indexterms</c> is read by
    /// /2's index-term rule into the article's metadata, and every other child is tokenized by this profile's rules.
    /// </summary>
    private static bool TryTokenize(
        XElement article,
        NoteIndex notes,
        out IReadOnlyList<LuxembourgAknLegalContentToken>? tokens,
        out IReadOnlyList<LuxembourgAknIndexTerm>? indexTerms,
        out string? failure)
    {
        var reader = new Reader(notes, inNote: false);
        var terms = new List<LuxembourgAknIndexTerm>();
        tokens = null;
        indexTerms = null;
        if (DeletionMarkedByAttribute(article) is { } markedArticle && HoldsWordingOutsideDeletion(article))
        {
            failure = AttributeDeletion + markedArticle;
            return false;
        }
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
                if (!LuxembourgAknLegalContentProfile2Producer.TryIndexTerms(block, terms, out failure))
                {
                    return false;
                }
                continue;
            }
            if (!reader.TryNode(node, out failure))
            {
                return false;
            }
        }
        if (reader.OpenModifications.Count != 0)
        {
            failure = "article ends with an unclosed modification span: "
                + string.Join(",", reader.OpenModifications.Order(StringComparer.Ordinal));
            return false;
        }
        if (reader.Tokens.Count == 0)
        {
            failure = "article contains no publisher legal wording or marker evidence";
            return false;
        }
        tokens = reader.Tokens;
        indexTerms = terms;
        failure = null;
        return true;
    }

    /// <summary>The publisher's notes of one document by id, read once per body on the first note reference.</summary>
    private sealed class NoteIndex(XDocument document)
    {
        private Dictionary<string, List<XElement>>? _byId;

        internal IReadOnlyList<XElement> Find(string id)
        {
            if (_byId is null)
            {
                _byId = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
                foreach (var note in document.Root!.Descendants(XName.Get("note", AknNamespace)))
                {
                    if ((string?)note.Attribute("id") is not { } noteId)
                    {
                        continue;
                    }
                    if (!_byId.TryGetValue(noteId, out var list))
                    {
                        _byId[noteId] = list = [];
                    }
                    list.Add(note);
                }
            }
            return _byId.TryGetValue(id, out var found) ? found : [];
        }
    }

    /// <summary>
    /// One token stream being read: an article's, or one note body's. It carries what the rules depend on, the depth of
    /// quotation, deleted wording and underline the node sits in, and the separator a layout marker leaves pending.
    /// </summary>
    private sealed class Reader(NoteIndex notes, bool inNote)
    {
        private int _quotationDepth;
        private int _deletedDepth;
        private int _underlineDepth;
        private (int Index, bool Deleted, bool Underline)? _pendingSeparator;

        internal List<LuxembourgAknLegalContentToken> Tokens { get; } = [];

        internal HashSet<string> OpenModifications { get; } = new(StringComparer.Ordinal);

        internal bool TryNode(XNode node, out string? failure)
        {
            failure = null;
            if (node is XText text)
            {
                if (!string.IsNullOrWhiteSpace(text.Value))
                {
                    Wording(_deletedDepth > 0
                        ? LuxembourgAknLegalContentTokenKind.DeletedText
                        : LuxembourgAknLegalContentTokenKind.Text, text.Value, null);
                }
                return true;
            }
            if (node is not XElement element || element.Name.NamespaceName != AknNamespace)
            {
                failure = Namespace0;
                return false;
            }

            var name = element.Name.LocalName;
            if (_deletedDepth == 0 && DeletionMarkedByAttribute(element) is { } marked && HoldsWordingOutsideDeletion(element))
            {
                failure = AttributeDeletion + marked;
                return false;
            }
            switch (name)
            {
                case "ref":
                    return TryReference(element, out failure);
                case "mod":
                    return TryModification(element, out failure);
                case "noteRef":
                    if (inNote)
                    {
                        failure = "publisher note bodies cannot recursively reference notes";
                        return false;
                    }
                    return TryNoteReference(element, out failure);
            }

            if (LayoutMarkers.Contains(name))
            {
                return TryLayoutMarker(element, out failure);
            }
            if (name == "u")
            {
                if (_quotationDepth > 0)
                {
                    failure = "underline inside quoted or amending wording: u";
                    return false;
                }
                _underlineDepth++;
                var underlined = TryChildren(element, out failure);
                _underlineDepth--;
                return underlined;
            }
            if (name == "del")
            {
                if ((string?)element.Attribute("class") is { } deletedClass)
                {
                    failure = "deleted wording of an unreviewed class: " + deletedClass;
                    return false;
                }
                return TryDeleted(element, out failure);
            }
            if (name == "span")
            {
                var spanClass = (string?)element.Attribute("class");
                if (spanClass is null)
                {
                    return TryChildren(element, out failure);
                }
                if (spanClass == "strike")
                {
                    return TryDeleted(element, out failure);
                }
                failure = "span of an unreviewed class: " + spanClass;
                return false;
            }
            if (Quotations.Contains(name))
            {
                return TryQuotation(element, out failure);
            }
            if (_deletedDepth > 0)
            {
                if (!InlineContainers.Contains(name))
                {
                    failure = InsideDeleted + name;
                    return false;
                }
                return TryChildren(element, out failure);
            }
            if (Containers.Contains(name) || (_quotationDepth > 0 && QuotedHierarchy.Contains(name)))
            {
                return TryChildren(element, out failure);
            }
            failure = "unsupported legal-content element: " + name;
            return false;
        }

        private bool TryChildren(XElement element, out string? failure)
        {
            foreach (var child in element.Nodes())
            {
                if (!TryNode(child, out failure))
                {
                    return false;
                }
            }
            failure = null;
            return true;
        }

        /// <summary>
        /// One token of wording (text, a reference, deleted wording), after the separator a layout marker left pending when
        /// this wording begins with a character that is not white space.
        /// </summary>
        private void Wording(LuxembourgAknLegalContentTokenKind kind, string text, string? target)
        {
            if (_pendingSeparator is { } pending && !char.IsWhiteSpace(text[0]))
            {
                Tokens.Insert(pending.Index, new LuxembourgAknLegalContentToken(
                    pending.Deleted ? LuxembourgAknLegalContentTokenKind.DeletedText : LuxembourgAknLegalContentTokenKind.Text,
                    " ", null, null)
                {
                    Underline = pending.Underline,
                });
            }
            _pendingSeparator = null;
            Tokens.Add(new LuxembourgAknLegalContentToken(kind, text, target, null) { Underline = _underlineDepth > 0 });
        }

        private bool TryLayoutMarker(XElement element, out string? failure)
        {
            if (element.Nodes().Any())
            {
                failure = "layout marker is not empty: " + element.Name.LocalName;
                return false;
            }
            if (_pendingSeparator is null)
            {
                var last = Tokens.LastOrDefault(static token => token.Kind is
                    LuxembourgAknLegalContentTokenKind.Text or
                    LuxembourgAknLegalContentTokenKind.Reference or
                    LuxembourgAknLegalContentTokenKind.DeletedText);
                if (last?.Text is { Length: > 0 } before && !char.IsWhiteSpace(before[^1]))
                {
                    _pendingSeparator = (Tokens.Count, _deletedDepth > 0, _underlineDepth > 0);
                }
            }
            failure = null;
            return true;
        }

        private bool TryDeleted(XElement element, out string? failure)
        {
            if (inNote)
            {
                failure = InsideNote + element.Name.LocalName;
                return false;
            }
            _deletedDepth++;
            var read = TryChildren(element, out failure);
            _deletedDepth--;
            return read;
        }

        private bool TryQuotation(XElement element, out string? failure)
        {
            var name = element.Name.LocalName;
            if (inNote)
            {
                failure = InsideNote + name;
                return false;
            }
            if (_deletedDepth > 0)
            {
                failure = InsideDeleted + name;
                return false;
            }

            bool marksInWording;
            switch ((string?)element.Attribute(XName.Get("hide-quote", SclNamespace)))
            {
                case null or "false":
                    marksInWording = false;
                    break;
                case "true":
                    marksInWording = true;
                    break;
                default:
                    failure = "quotation carries an unreviewed scl:hide-quote value";
                    return false;
            }
            var startQuote = (string?)element.Attribute("startQuote");
            var endQuote = (string?)element.Attribute("endQuote");
            if ((startQuote is not null && startQuote.Length is 0 or > 64) ||
                (endQuote is not null && endQuote.Length is 0 or > 64))
            {
                failure = "quotation mark is not a bounded publisher mark";
                return false;
            }

            Tokens.Add(new LuxembourgAknLegalContentToken(
                LuxembourgAknLegalContentTokenKind.QuotationStart, null, null, startQuote)
            {
                Quotation = name,
                MarksInWording = marksInWording,
            });
            _quotationDepth++;
            var read = TryChildren(element, out failure);
            _quotationDepth--;
            if (!read)
            {
                return false;
            }
            Tokens.Add(new LuxembourgAknLegalContentToken(
                LuxembourgAknLegalContentTokenKind.QuotationEnd, null, null, endQuote)
            {
                Quotation = name,
                MarksInWording = marksInWording,
            });
            return true;
        }

        /// <summary>/1's reference rule; inside deleted wording the reference is deleted wording that keeps its target.</summary>
        private bool TryReference(XElement element, out string? failure)
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
            Wording(_deletedDepth > 0
                ? LuxembourgAknLegalContentTokenKind.DeletedText
                : LuxembourgAknLegalContentTokenKind.Reference, label, target);
            failure = null;
            return true;
        }

        /// <summary>/1's modification rule: an empty boundary, paired within the stream it is read in.</summary>
        private bool TryModification(XElement element, out string? failure)
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
                if (!OpenModifications.Add(target))
                {
                    failure = "duplicate open modification span: " + target;
                    return false;
                }
                Tokens.Add(new(LuxembourgAknLegalContentTokenKind.ModificationStart, null, target, null));
                failure = null;
                return true;
            }
            if (kind == "mod-end")
            {
                if (!OpenModifications.Remove(target))
                {
                    failure = "modification end has no start in this article: " + target;
                    return false;
                }
                Tokens.Add(new(LuxembourgAknLegalContentTokenKind.ModificationEnd, null, target, null));
                failure = null;
                return true;
            }
            failure = "unsupported modification class: " + kind;
            return false;
        }

        /// <summary>
        /// /1's note rule for a reference with a marker; a reference without one binds exactly one note by its id and is
        /// flagged unmarked. The body is read by these rules, in its own stream, with no quotation and no deleted wording.
        /// </summary>
        private bool TryNoteReference(XElement element, out string? failure)
        {
            var target = (string?)element.Attribute("href");
            var marker = (string?)element.Attribute("marker");
            if (element.Nodes().Any() || !Bound(target) || (marker is not null && !Bound(marker)) || !target!.StartsWith('#'))
            {
                failure = "note reference is not empty or lacks bounded publisher evidence";
                return false;
            }
            var matches = notes.Find(target[1..]);
            if (marker is not null &&
                (matches.Count != 1 ||
                 !string.Equals((string?)matches[0].Attribute("marker"), marker, StringComparison.Ordinal)))
            {
                failure = "note reference does not bind exactly one publisher note with the same marker";
                return false;
            }
            if (marker is null && matches.Count != 1)
            {
                failure = "unmarked note reference does not bind exactly one publisher note";
                return false;
            }

            var body = new Reader(notes, inNote: true);
            foreach (var child in matches[0].Nodes())
            {
                if (!body.TryNode(child, out failure))
                {
                    return false;
                }
            }
            if (body.OpenModifications.Count != 0 ||
                !body.Tokens.Any(static token => token.Kind is
                    LuxembourgAknLegalContentTokenKind.Text or LuxembourgAknLegalContentTokenKind.Reference))
            {
                failure = "publisher note has no preserved wording or has an unclosed modification span";
                return false;
            }
            Tokens.Add(new LuxembourgAknLegalContentToken(
                LuxembourgAknLegalContentTokenKind.NoteReference,
                null,
                target,
                marker,
                Array.AsReadOnly(body.Tokens.ToArray()))
            {
                Unmarked = marker is null,
            });
            failure = null;
            return true;
        }
    }

    /// <summary>
    /// The attribute that marks <paramref name="element"/> deleted, as <c>style=del on paragraph</c>, or null: <c>style="del"</c>
    /// on any element, or <c>class="strike"</c> on anything but a <c>span</c>; never on <c>del</c> itself, which is read as
    /// deleted whatever it carries.
    /// </summary>
    private static string? DeletionMarkedByAttribute(XElement element)
    {
        var name = element.Name.LocalName;
        if (name == "del")
        {
            return null;
        }
        if ((string?)element.Attribute("style") == "del")
        {
            return "style=del on " + name;
        }
        if (name != "span" && (string?)element.Attribute("class") == "strike")
        {
            return "class=strike on " + name;
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="element"/> holds a character of AKN wording that no <c>del</c> or struck span inside it marks
    /// deleted. Text under another namespace (<c>scl:JOLUXWork</c>, <c>scl:indexterms</c>) is not wording.
    /// </summary>
    private static bool HoldsWordingOutsideDeletion(XElement element)
    {
        foreach (var text in element.DescendantNodes().OfType<XText>())
        {
            if (string.IsNullOrWhiteSpace(text.Value))
            {
                continue;
            }
            var counts = true;
            for (var parent = text.Parent; parent is not null && parent != element; parent = parent.Parent)
            {
                if (parent.Name.NamespaceName != AknNamespace || parent.Name.LocalName == "del" ||
                    (parent.Name.LocalName == "span" && (string?)parent.Attribute("class") == "strike"))
                {
                    counts = false;
                    break;
                }
            }
            if (counts)
            {
                return true;
            }
        }
        return false;
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
