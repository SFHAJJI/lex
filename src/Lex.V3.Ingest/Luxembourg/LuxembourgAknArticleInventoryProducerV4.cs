using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// What a document's own identification block says about whose file it is, relative to the state
/// the run fetched it as. Evidence only: inventory /4 never refuses a file over its class.
/// </summary>
public enum LuxembourgAknInFileIdentityClass
{
    /// <summary>The block names the fetched state itself.</summary>
    [JsonStringEnumMemberName("own")]
    Own = 1,

    /// <summary>The block names only the work's base act (<c>/jo</c>), as many consolidations do.</summary>
    [JsonStringEnumMemberName("same_work_act")]
    SameWorkAct = 2,

    /// <summary>
    /// The block names only states of the same work, at least one of them a consolidation or a
    /// code state: an earlier consolidation and another code date are the same relation.
    /// </summary>
    [JsonStringEnumMemberName("same_work_other_state")]
    SameWorkOtherState = 3,

    /// <summary>The block names another work, among them a coordinated text or a republication.</summary>
    [JsonStringEnumMemberName("tc_or_trep")]
    TcOrTrep = 4,

    /// <summary>The block names another work, none of them a coordinated text or a republication.</summary>
    [JsonStringEnumMemberName("other_work")]
    OtherWork = 5,

    /// <summary>The block exists and names no state: the publisher's empty template.</summary>
    [JsonStringEnumMemberName("template_empty")]
    TemplateEmpty = 6,

    /// <summary>The document has no own identification block.</summary>
    [JsonStringEnumMemberName("absent")]
    Absent = 7,
}

/// <summary>The six content-consistency checks of inventory /4, in the design's order (a) to (f).</summary>
public enum LuxembourgAknContentConsistencyCheck
{
    /// <summary>(a) Another held input carries the same bytes.</summary>
    [JsonStringEnumMemberName("shared_file")]
    SharedFile = 1,

    /// <summary>(b) The block names a later state of the same work.</summary>
    [JsonStringEnumMemberName("later_state")]
    LaterState = 2,

    /// <summary>(c) The block names another work and the file's title does not designate this one.</summary>
    [JsonStringEnumMemberName("other_work")]
    OtherWork = 3,

    /// <summary>(d) The file's own applicability date is on or after the catalogue start of the work's next state.</summary>
    [JsonStringEnumMemberName("date_ahead")]
    DateAhead = 4,

    /// <summary>(e) The file is worded exactly as another held state of the work that it names.</summary>
    [JsonStringEnumMemberName("copied_state")]
    CopiedState = 5,

    /// <summary>(f) A later consolidation is worded exactly as the base act it names.</summary>
    [JsonStringEnumMemberName("copied_act")]
    CopiedAct = 6,
}

/// <summary>One fired content-consistency check and the state it names.</summary>
public sealed record LuxembourgAknContentConsistencyFinding(
    LuxembourgAknContentConsistencyCheck Check,
    string NamedState)
{
    public LuxembourgAknContentConsistencyCheck Check { get; } = Enum.IsDefined(Check)
        ? Check
        : throw new ArgumentOutOfRangeException(nameof(Check));

    public string NamedState { get; } = string.IsNullOrEmpty(NamedState)
        ? throw new ArgumentException("A finding names the state it found.", nameof(NamedState))
        : NamedState;
}

/// <summary>
/// What a document's own identification block says, kept as evidence beside an inventory /4. It is
/// never a state coordinate: the state a body serves comes from the run's catalogue.
/// </summary>
public sealed class LuxembourgAknInventoryEvidence
{
    public LuxembourgAknInventoryEvidence(
        LuxembourgAknInFileIdentityClass identityClass,
        IReadOnlyList<string> namedStates,
        string? inFileLegalResourceIri,
        string? inFileApplicabilityDate,
        int excludedArticleCount)
    {
        if (!Enum.IsDefined(identityClass))
        {
            throw new ArgumentOutOfRangeException(nameof(identityClass));
        }

        ArgumentNullException.ThrowIfNull(namedStates);
        var states = namedStates.ToArray();
        foreach (var state in states)
        {
            if (string.IsNullOrEmpty(state))
            {
                throw new ArgumentException("A named state is a non-empty IRI.", nameof(namedStates));
            }
        }

        ArgumentOutOfRangeException.ThrowIfNegative(excludedArticleCount);
        IdentityClass = identityClass;
        NamedStates = Array.AsReadOnly(states);
        InFileLegalResourceIri = inFileLegalResourceIri;
        InFileApplicabilityDate = inFileApplicabilityDate;
        ExcludedArticleCount = excludedArticleCount;
    }

    public LuxembourgAknInFileIdentityClass IdentityClass { get; }

    /// <summary>
    /// The states the own block's manifestations name (<c>uriThis</c> less a trailing
    /// <c>/ll/(xml|pdf|html|docx)</c>), ordinal-distinct, in document order.
    /// </summary>
    public IReadOnlyList<string> NamedStates { get; }

    /// <summary>The single legal resource's single <c>uriThis</c>, or null.</summary>
    public string? InFileLegalResourceIri { get; }

    /// <summary>The single legal resource's single exact <c>dateApplicability</c>, or null.</summary>
    public string? InFileApplicabilityDate { get; }

    /// <summary>
    /// Articles with no article ancestor that are not main-body articles: annex, component and
    /// quoted articles, counted and never read.
    /// </summary>
    public int ExcludedArticleCount { get; }
}

/// <summary>
/// The kind, work and date a Legilux ELI state IRI spells. Methods only: S6 reuses them, and a static
/// class holding no field is not a closed surface.
/// </summary>
internal static class LuxembourgEliState
{
    /// <summary>
    /// <c>act</c> for <c>.../jo</c>, <c>consolidation</c> for <c>.../consolide/yyyymmdd</c>,
    /// <c>code</c> for <c>.../code/name/yyyymmdd</c>, otherwise <c>none</c>.
    /// </summary>
    internal static string KindOf(string? iri) =>
        iri is null ? "none"
        : iri.EndsWith("/jo", StringComparison.Ordinal) ? "act"
        : Regex.IsMatch(iri, "/consolide/[0-9]{8}$") ? "consolidation"
        : Regex.IsMatch(iri, "/code/[a-z_0-9]+/[0-9]{8}$") ? "code"
        : "none";

    internal static bool IsAct(string? iri) => KindOf(iri) == "act";

    internal static bool IsConsolidation(string? iri) => KindOf(iri) == "consolidation";

    internal static bool IsCode(string? iri) => KindOf(iri) == "code";

    /// <summary>A dated state: a consolidation or a code state.</summary>
    internal static bool IsConsolidationOrCode(string? iri) => KindOf(iri) is "consolidation" or "code";

    /// <summary>The work an act, consolidation or code state belongs to, or null.</summary>
    internal static string? WorkOf(string? iri)
    {
        if (iri is null)
        {
            return null;
        }

        var match = Regex.Match(iri, "^(.*)/jo$");
        if (!match.Success)
        {
            match = Regex.Match(iri, "^(.*)/consolide/[0-9]{8}$");
        }
        if (!match.Success)
        {
            match = Regex.Match(iri, "^(.*/code/[a-z_0-9]+)/[0-9]{8}$");
        }
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>A consolidation's or code state's last eight digits as <c>yyyy-MM-dd</c>, or null.</summary>
    internal static string? UriDateOf(string? iri)
    {
        if (!IsConsolidationOrCode(iri))
        {
            return null;
        }

        var match = Regex.Match(iri!, "/([0-9]{4})([0-9]{2})([0-9]{2})$");
        return match.Success
            ? match.Groups[1].Value + "-" + match.Groups[2].Value + "-" + match.Groups[3].Value
            : null;
    }
}

/// <summary>
/// Inventory /4: reopens every proof-bound held Luxembourg body, inventories its main-body articles,
/// keeps what its own identification block says as evidence, and refuses a body only when the file
/// is shown to belong to, or copy, another state.
/// </summary>
/// <remarks>
/// <para>
/// WHAT CHANGED FROM /3, which stays frozen beside this. /3 read every identification block in the
/// file, refused a file whose block disagreed with the selected WEMI, minted the state coordinate
/// from the file, admitted only printable ASCII ids and counted annex, component and quoted
/// articles as the document's own. /4 reads only the document's own block and never refuses over
/// it, never mints a coordinate (the catalogue dates a state), admits any publisher token as an id
/// and counts only the main body's top-level articles.
/// </para>
/// <para>
/// WHAT REPLACES THE IDENTITY CHECKS. Six content-consistency checks, evaluated in a second pass
/// over everything the first pass read, so no body is read twice. Every check that fires is
/// recorded, and any of them gives <see cref="LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState"/>
/// with the inventory kept as evidence.
/// </para>
/// <para>
/// THE DOCUMENT CEILING IS THE CUSTODY BOUND, NOT /3'S 64M CHARACTERS. Two run-15 bodies carry an
/// image in one attribute and exceed 64M characters: <c>rgd/2021/06/09/a444/jo</c> (175,837,349
/// bytes, 13,034 characters of text) and <c>rgd/2023/11/10/a762/jo</c> (110,151,932 bytes, 11,646).
/// /3 rejects both as <c>XmlException</c>; the reference implementation the slice's acceptance was
/// measured with reads them. A document cannot hold more characters than its bytes, and custody
/// never holds an object above <see cref="CustodyBounds.MaxObjectBytes"/>, so this ceiling bounds
/// exactly what custody bounds. The parser's other settings are /3's.
/// </para>
/// </remarks>
public sealed class LuxembourgAknArticleInventoryProducerV4
{
    private const string AknNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    private const string SclNamespace = "http://www.scl.lu";
    private const long MaximumXmlCharacters = CustodyBounds.MaxObjectBytes;
    private const int MaximumPublisherTokenLength = 512;

    /// <summary>
    /// The title grammar of check (c). The profile line and the matcher are both built from this one
    /// constant, so a grammar change always changes the rule digest.
    /// </summary>
    private const string TitleDesignationPattern =
        "\\b(r[e\u00E8]glement grand-ducal|arr[e\u00EA]t[e\u00E9] grand-ducal|r[e\u00E8]glement minist[e\u00E9]riel|arr[e\u00EA]t[e\u00E9] minist[e\u00E9]riel|loi)s?(?: (?:modifi[e\u00E9]e?s?|communale|[e\u00E9]lectorale|g[e\u00E9]n[e\u00E9]rale|organique|budg[e\u00E9]taire))* du (1 ?er|[0-9]{1,2}) (janvier|f[e\u00E9]vrier|mars|avril|mai|juin|juillet|ao[u\u00FB]t|septembre|octobre|novembre|d[e\u00E9]cembre) ([0-9]{4})\\b";

    private const string RuleProfile =
        "lex-v3-luxembourg-akn-article-inventory-profile/4\n" +
        "formats=xml-akomantoso,xml\n" +
        "namespace=http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\n" +
        "document=the-single-akn-element-child-of-akomaNtoso-other-than-components;exactly-one-body\n" +
        "identification=document-meta-identification-only;at-most-one;evidence-never-rejection\n" +
        "named-states=own-block-JOLUXManifestation-uriThis-less-trailing-/ll/(xml|pdf|html|docx)\n" +
        "identity-class=own,same_work_act,same_work_other_state,tc_or_trep,other_work,template_empty,absent\n" +
        "in-file-date=the-single-exact-dateApplicability-of-the-single-JOLUXLegalResource\n" +
        "state-coordinate=not-minted-from-the-file\n" +
        "articles=body-articles-without-article-or-quotation-ancestor;publisher-order;at-least-one\n" +
        "quotations=mod,quotedStructure,quotedText,embeddedStructure,embeddedText\n" +
        "other-top-level-articles=counted-never-read\n" +
        "id=publisher-token;1-512-utf16-code-units;never-normalized\n" +
        "id-forbidden=U+0000-0020,U+007F-00A0,U+061C,U+1680,U+2000-200F,U+2028-202F,U+205F-2060,U+2066-2069,U+3000,U+FEFF\n" +
        "wid=publisher-token-optional-distinct\n" +
        "applicability=scl-jolux-scl-name-dateApplicability-article-owned-single\n" +
        "wording=main-body-article-id-and-text-then-annex-containers;text=character-data-outside-scl-elements-joined-by-one-space;xml-white-space-collapsed;no-unicode-normalization\n" +
        "catalogue=typed-assertion-dateApplicability;one-exact-date-per-consolidation-or-code-state\n" +
        "consistency=shared_file,later_state,other_work,date_ahead,copied_state,copied_act;every-fired-check-recorded;any-refuses\n" +
        "title-designation=first-own-block-title;white-space-runs-as-one-space;lowercase-invariant;leftmost=" +
        TitleDesignationPattern + "\n" +
        "refusal=publisher_file_names_other_state;inventory-kept-as-evidence\n";

    private const string ContentConsistencyDetail = "content consistency: ";

    private static readonly string RuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(RuleProfile)));

    private readonly ICustodyStore _custodyStore;

    public LuxembourgAknArticleInventoryProducerV4(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 => RuleDigest;

    public static byte[] CopyRuleProfileBytes() => Encoding.UTF8.GetBytes(RuleProfile);

    /// <summary>
    /// The production door: the run's own held-body population, dated by the run's own catalogue.
    /// No caller supplies either, so an inventory cannot be dated by a catalogue the run did not
    /// observe.
    /// </summary>
    public Task<LuxembourgAknArticleInventoryPopulation> RunAsync(
        LuxembourgQueryExecutionResult run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Refusal is not null || run.HeldBodyDerivationPopulation is not { } population)
        {
            throw new ArgumentException(
                "Inventory /4 reads only a delivered run's own held-body population.", nameof(run));
        }

        return RunAsync(
            population,
            CatalogueStarts(CatalogueDateApplicability(run.TypedAssertions)),
            cancellationToken);
    }

    /// <summary>
    /// The core, for tests and the read-only probe: a held-body population and the catalogue starts
    /// of its consolidation and code states, keyed by state IRI.
    /// </summary>
    internal async Task<LuxembourgAknArticleInventoryPopulation> RunAsync(
        LuxembourgHeldBodyDerivationPopulation population,
        IReadOnlyDictionary<string, string> catalogueStarts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(catalogueStarts);
        var inputs = population.Inputs.OrderBy(static input => input.ObjectOrdinal).ToArray();
        var dispositions = new LuxembourgAknArticleInventoryDisposition[inputs.Length];
        var inventories = new LuxembourgAknArticleInventory?[inputs.Length];
        var details = new string?[inputs.Length];
        var wordings = new string?[inputs.Length];
        var titlesDesignateRoot = new bool[inputs.Length];

        // Pass 1: every body is read once: structure, evidence, wording and title.
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[index];
            if (input.Address.UserFormatToken is not (
                    LuxembourgUserFormatToken.XmlAkomaNtoso or LuxembourgUserFormatToken.Xml))
            {
                dispositions[index] = LuxembourgAknArticleInventoryDisposition.NotAkn;
                details[index] = TokenName(input.Address.UserFormatToken);
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
                dispositions[index] = LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable;
                details[index] = exception.Message;
                continue;
            }

            if (!TryInventory(input, bytes, out var inventory, out var wording,
                    out var titleDesignatesRoot, out var failure))
            {
                dispositions[index] = LuxembourgAknArticleInventoryDisposition.XmlRejected;
                details[index] = failure;
                continue;
            }

            dispositions[index] = LuxembourgAknArticleInventoryDisposition.Inventoried;
            inventories[index] = inventory;
            wordings[index] = wording;
            titlesDesignateRoot[index] = titleDesignatesRoot;
        }

        // Pass 2: the checks, over what pass 1 read and the catalogue.
        var sharers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var inventoriedByRoot = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < inputs.Length; index++)
        {
            Add(sharers, inputs[index].Receipt.Reference.ContentSha256, index);
            if (dispositions[index] == LuxembourgAknArticleInventoryDisposition.Inventoried)
            {
                Add(inventoriedByRoot, inputs[index].CorpusRecord.ObjectRef.PublisherUri, index);
            }
        }

        var startsByWork = StartsByWork(catalogueStarts);
        var outcomes = new List<LuxembourgAknArticleInventoryOutcome>(inputs.Length);
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[index];
            if (dispositions[index] != LuxembourgAknArticleInventoryDisposition.Inventoried)
            {
                outcomes.Add(new(input, dispositions[index], null, details[index]));
                continue;
            }

            var inventory = inventories[index]!;
            var evidence = inventory.Evidence!;
            var root = input.CorpusRecord.ObjectRef.PublisherUri;
            var work = LuxembourgEliState.WorkOf(root);
            var rootDated = LuxembourgEliState.IsConsolidationOrCode(root);
            var findings = new List<LuxembourgAknContentConsistencyFinding>();

            // (a) Another held input has the same bytes: one finding per other sharer, in ordinal order.
            foreach (var sharer in sharers[input.Receipt.Reference.ContentSha256])
            {
                if (sharer != index)
                {
                    findings.Add(new(
                        LuxembourgAknContentConsistencyCheck.SharedFile,
                        inputs[sharer].CorpusRecord.ObjectRef.PublisherUri));
                }
            }

            var sameWork = new List<string>();
            var otherWork = new List<string>();
            foreach (var state in evidence.NamedStates)
            {
                if (string.Equals(state, root, StringComparison.Ordinal))
                {
                    continue;
                }

                if (work is not null &&
                    string.Equals(LuxembourgEliState.WorkOf(state), work, StringComparison.Ordinal))
                {
                    sameWork.Add(state);
                }
                else
                {
                    otherWork.Add(state);
                }
            }

            // (b) A later state of the same work; for an act, any consolidation or code state of it.
            foreach (var state in sameWork)
            {
                if (rootDated && LuxembourgEliState.IsConsolidationOrCode(state) &&
                    string.CompareOrdinal(LuxembourgEliState.UriDateOf(state), LuxembourgEliState.UriDateOf(root)) > 0)
                {
                    findings.Add(new(LuxembourgAknContentConsistencyCheck.LaterState, state));
                }
                else if (LuxembourgEliState.IsAct(root) && LuxembourgEliState.IsConsolidationOrCode(state))
                {
                    findings.Add(new(LuxembourgAknContentConsistencyCheck.LaterState, state));
                }
            }

            // (c) Another work, and the root is a code (whose title has no dated designation) or the
            // file's title does not designate the root's instrument.
            if (otherWork.Count != 0 &&
                (LuxembourgEliState.IsCode(root) || !titlesDesignateRoot[index]))
            {
                foreach (var state in otherWork)
                {
                    findings.Add(new(LuxembourgAknContentConsistencyCheck.OtherWork, state));
                }
            }

            var start = rootDated
                ? catalogueStarts.TryGetValue(root, out var catalogued) ? catalogued : LuxembourgEliState.UriDateOf(root)
                : null;
            var workStarts = work is not null && startsByWork.TryGetValue(work, out var found) ? found : null;

            // (d) The in-file date is on or after the catalogue start of the work's next state.
            if (rootDated && evidence.InFileApplicabilityDate is { } inFileDate && workStarts is not null)
            {
                foreach (var (nextStart, nextState) in workStarts)
                {
                    if (string.CompareOrdinal(nextStart, start) <= 0)
                    {
                        continue;
                    }

                    if (string.CompareOrdinal(inFileDate, nextStart) >= 0)
                    {
                        findings.Add(new(LuxembourgAknContentConsistencyCheck.DateAhead, nextState));
                    }
                    break;
                }
            }

            // (e) Worded exactly as another held, inventoried consolidation or code state it names.
            if (rootDated)
            {
                foreach (var state in sameWork)
                {
                    if (LuxembourgEliState.IsConsolidationOrCode(state) &&
                        inventoriedByRoot.TryGetValue(state, out var holders) &&
                        AnySameWording(holders, wordings, wordings[index]!))
                    {
                        findings.Add(new(LuxembourgAknContentConsistencyCheck.CopiedState, state));
                    }
                }
            }

            // (f) A consolidation worded exactly as the base act it names, after an earlier consolidation.
            if (LuxembourgEliState.IsConsolidation(root) && work is not null)
            {
                var act = work + "/jo";
                if (sameWork.Contains(act) &&
                    HasEarlierConsolidation(workStarts, start!) &&
                    inventoriedByRoot.TryGetValue(act, out var holders) &&
                    AnySameWording(holders, wordings, wordings[index]!))
                {
                    findings.Add(new(LuxembourgAknContentConsistencyCheck.CopiedAct, act));
                }
            }

            if (findings.Count == 0)
            {
                outcomes.Add(new(input, LuxembourgAknArticleInventoryDisposition.Inventoried, inventory, null));
                continue;
            }

            outcomes.Add(new(
                input,
                LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState,
                inventory,
                ContentConsistencyDetail + string.Join(
                    ",",
                    findings.Select(static finding => ContractWire.NameOf(finding.Check)).Distinct(StringComparer.Ordinal)),
                findings));
        }

        return new LuxembourgAknArticleInventoryPopulation(population, outcomes, RuleDigest);
    }

    /// <summary>
    /// The catalogue's dated states: a consolidation or code state enters with exactly one distinct
    /// raw <c>dateApplicability</c> value that is an exact civil date.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> CatalogueStarts(
        IEnumerable<(string SubjectIri, string RawLexicalValue)> dateApplicabilityFacts)
    {
        ArgumentNullException.ThrowIfNull(dateApplicabilityFacts);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (subject, value) in dateApplicabilityFacts)
        {
            if (!LuxembourgEliState.IsConsolidationOrCode(subject))
            {
                continue;
            }

            if (!values.TryGetValue(subject, out var distinct))
            {
                distinct = [];
                values.Add(subject, distinct);
            }
            if (!distinct.Contains(value, StringComparer.Ordinal))
            {
                distinct.Add(value);
            }
        }

        var starts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (subject, distinct) in values)
        {
            if (distinct.Count == 1 && ExactCivilDate(distinct[0]))
            {
                starts.Add(subject, distinct[0]);
            }
        }
        return starts;
    }

    /// <summary>
    /// The run's <c>dateApplicability</c> facts: each typed assertion of that predicate that carries
    /// its consolidation applicability date, as (subject, raw lexical value).
    /// </summary>
    internal static IReadOnlyList<(string SubjectIri, string RawLexicalValue)> CatalogueDateApplicability(
        IReadOnlyList<LuxembourgTypedAssertion> typedAssertions)
    {
        ArgumentNullException.ThrowIfNull(typedAssertions);
        var facts = new List<(string SubjectIri, string RawLexicalValue)>();
        foreach (var assertion in typedAssertions)
        {
            if (assertion.FactDisposition.Predicate == LuxembourgAssertionPredicate.DateApplicability &&
                assertion.ConsolidationApplicabilityDateFact is { } fact)
            {
                facts.Add((assertion.Assertion.SubjectIri, fact.RawLexicalValue));
            }
        }
        return facts;
    }

    /// <summary>
    /// Steps 4 to 6 of the structural reading: the AKN root, its single document element (any AKN
    /// element child other than <c>components</c>) and that element's single main body.
    /// </summary>
    internal static bool TryDocument(
        XDocument document,
        out XElement? documentElement,
        out XElement? body,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(document);
        documentElement = null;
        body = null;
        failure = null;
        XNamespace akn = AknNamespace;
        var root = document.Root;
        if (root?.Name != akn + "akomaNtoso")
        {
            failure = "publisher XML root or namespace is not the accepted AKN namespace";
            return false;
        }

        XElement? only = null;
        var count = 0;
        foreach (var child in root.Elements())
        {
            if (child.Name.Namespace == akn && child.Name.LocalName != "components")
            {
                only = child;
                count++;
            }
        }
        if (count != 1)
        {
            failure = "publisher AKN has no single document element";
            return false;
        }

        var bodies = only!.Elements(akn + "body").ToArray();
        if (bodies.Length != 1)
        {
            failure = "publisher AKN document has no single main body";
            return false;
        }

        documentElement = only;
        body = bodies[0];
        return true;
    }

    /// <summary>
    /// The document's own articles: the article descendants of the main body with no article or
    /// quotation element between them and the body, in document order. An article quoted by an
    /// amending act is another instrument's article, so it is never this document's.
    /// </summary>
    internal static IReadOnlyList<XElement> MainBodyArticles(XElement body)
    {
        ArgumentNullException.ThrowIfNull(body);
        XNamespace akn = AknNamespace;
        var selected = new List<XElement>();
        foreach (var article in body.Descendants(akn + "article"))
        {
            var own = true;
            for (var ancestor = article.Parent; ancestor is not null && ancestor != body; ancestor = ancestor.Parent)
            {
                if (ancestor.Name.Namespace == akn && ancestor.Name.LocalName is
                        "article" or "mod" or "quotedStructure" or "quotedText" or
                        "embeddedStructure" or "embeddedText")
                {
                    own = false;
                    break;
                }
            }
            if (own)
            {
                selected.Add(article);
            }
        }
        return selected.AsReadOnly();
    }

    /// <summary>
    /// The wording digest checks (e) and (f) compare, for a body whose structural reading finds its
    /// main-body articles and their ids; otherwise null. Never stored.
    /// </summary>
    internal static string? WordingSha256(ReadOnlyMemory<byte> bytes)
    {
        if (!TryLoad(bytes, out var document, out _) ||
            !TryDocument(document!, out var documentElement, out var body, out _))
        {
            return null;
        }

        var articles = MainBodyArticles(body!);
        if (articles.Count == 0)
        {
            return null;
        }

        var ids = new string[articles.Count];
        for (var index = 0; index < articles.Count; index++)
        {
            var id = (string?)articles[index].Attribute("id");
            if (!PublisherToken(id))
            {
                return null;
            }
            ids[index] = id!;
        }
        return Wording(document!.Root!, documentElement!, articles, ids);
    }

    private static bool TryInventory(
        LuxembourgHeldBodyDerivationInput input,
        ReadOnlyMemory<byte> bytes,
        out LuxembourgAknArticleInventory? inventory,
        out string? wordingSha256,
        out bool titleDesignatesRoot,
        out string? failure)
    {
        inventory = null;
        wordingSha256 = null;
        titleDesignatesRoot = false;
        if (!TryLoad(bytes, out var document, out failure) ||
            !TryDocument(document!, out var documentElement, out var body, out failure))
        {
            return false;
        }

        var articles = MainBodyArticles(body!);
        if (articles.Count == 0)
        {
            failure = "publisher XML contains no main-body article";
            return false;
        }

        XNamespace akn = AknNamespace;
        var ownBlocks = documentElement!.Elements(akn + "meta").Elements(akn + "identification").ToArray();
        if (ownBlocks.Length > 1)
        {
            failure = "publisher AKN document has more than one own identification block";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var wids = new HashSet<string>(StringComparer.Ordinal);
        var coordinates = new List<LuxembourgAknArticleCoordinate>(articles.Count);
        foreach (var element in articles)
        {
            var id = (string?)element.Attribute("id");
            if (!PublisherToken(id))
            {
                failure = "main-body article has no valid publisher id";
                return false;
            }
            if (!ids.Add(id!))
            {
                failure = "duplicate publisher id: " + id;
                return false;
            }

            var wid = (string?)element.Attribute("wId");
            if (wid is not null && !PublisherToken(wid))
            {
                failure = "main-body article has an invalid publisher wId";
                return false;
            }
            if (wid is not null && !wids.Add(wid))
            {
                failure = "duplicate publisher wId: " + wid;
                return false;
            }

            var applicability = new List<string>();
            foreach (var value in element.Descendants(XName.Get("jolux", SclNamespace)))
            {
                if (!string.Equals(
                        (string?)value.Attribute(XName.Get("name", SclNamespace)),
                        "dateApplicability", StringComparison.Ordinal) ||
                    !ReferenceEquals(NearestArticle(value), element))
                {
                    continue;
                }

                var text = value.Value.Trim();
                if (text.Length != 0 && !applicability.Contains(text))
                {
                    applicability.Add(text);
                }
            }
            if (applicability.Count > 1)
            {
                failure = "article has conflicting applicability values: " + id;
                return false;
            }
            coordinates.Add(new(id!, wid, applicability.Count == 0 ? null : applicability[0]));
        }

        var topLevel = 0;
        foreach (var article in document!.Root!.Descendants(akn + "article"))
        {
            if (NearestArticle(article) is null)
            {
                topLevel++;
            }
        }

        var root = input.CorpusRecord.ObjectRef.PublisherUri;
        LuxembourgAknInventoryEvidence evidence;
        if (ownBlocks.Length == 0)
        {
            evidence = new(LuxembourgAknInFileIdentityClass.Absent, [], null, null, topLevel - articles.Count);
        }
        else
        {
            var block = ownBlocks[0];
            var named = NamedStates(block);
            var legalResources = block.Descendants(XName.Get("JOLUXLegalResource", SclNamespace)).ToArray();
            string? legalResource = null;
            string? date = null;
            if (legalResources.Length == 1)
            {
                var uris = NamedValues(legalResources[0], "uriThis");
                legalResource = uris.Count == 1 ? uris[0] : null;
                var dates = NamedValues(legalResources[0], "dateApplicability");
                date = dates.Count == 1 && ExactCivilDate(dates[0]) ? dates[0] : null;
            }

            var titles = NamedValues(block, "title");
            var designation = titles.Count == 0 ? null : TitleDesignation(titles[0]);
            titleDesignatesRoot = designation is not null &&
                string.Equals(designation, EliDesignation(root), StringComparison.Ordinal);
            evidence = new(ClassOf(root, named), named, legalResource, date, topLevel - articles.Count);
        }

        wordingSha256 = Wording(
            document.Root!,
            documentElement,
            articles,
            coordinates.Select(static coordinate => coordinate.PublisherId).ToArray());
        inventory = new LuxembourgAknArticleInventory(
            input.SelectedWemiCandidate.ExpressionIri,
            RuleDigest,
            coordinates,
            null,
            null,
            null,
            evidence);
        return true;
    }

    private static bool TryLoad(ReadOnlyMemory<byte> bytes, out XDocument? document, out string? failure)
    {
        document = null;
        failure = null;
        try
        {
            using var stream = MemoryMarshal.TryGetArray(bytes, out var segment)
                ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaximumXmlCharacters,
                IgnoreComments = true,
            });
            document = XDocument.Load(reader, LoadOptions.None);
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            failure = "publisher XML rejected: " + exception.GetType().Name;
            return false;
        }
    }

    /// <summary>The nearest AKN article strictly above <paramref name="node"/>, or null.</summary>
    private static XElement? NearestArticle(XElement node)
    {
        XNamespace akn = AknNamespace;
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Name == akn + "article")
            {
                return ancestor;
            }
        }
        return null;
    }

    /// <summary>
    /// The <c>scl:jolux</c> values named <paramref name="name"/> under <paramref name="element"/>:
    /// trimmed, non-empty, ordinal-distinct, in document order (/3's reading).
    /// </summary>
    private static IReadOnlyList<string> NamedValues(XElement element, string name)
    {
        var values = new List<string>();
        foreach (var value in element.Descendants(XName.Get("jolux", SclNamespace)))
        {
            if (!string.Equals(
                    (string?)value.Attribute(XName.Get("name", SclNamespace)), name, StringComparison.Ordinal))
            {
                continue;
            }

            var text = value.Value.Trim();
            if (text.Length != 0 && !values.Contains(text))
            {
                values.Add(text);
            }
        }
        return values;
    }

    /// <summary>
    /// The states the own block's manifestations name: each <c>uriThis</c> less a trailing
    /// <c>/ll/(xml|pdf|html|docx)</c>, ordinal-distinct, in document order. A value the suffix does not
    /// match is kept whole, and so is one the suffix would leave empty.
    /// </summary>
    private static IReadOnlyList<string> NamedStates(XElement block)
    {
        var states = new List<string>();
        foreach (var manifestation in block.Descendants(XName.Get("JOLUXManifestation", SclNamespace)))
        {
            foreach (var value in NamedValues(manifestation, "uriThis"))
            {
                var match = Regex.Match(value, "^(.*)/[a-z]{2}/(xml|pdf|html|docx)$");
                var state = match.Success && match.Groups[1].Length != 0 ? match.Groups[1].Value : value;
                if (!states.Contains(state))
                {
                    states.Add(state);
                }
            }
        }
        return states;
    }

    private static LuxembourgAknInFileIdentityClass ClassOf(string root, IReadOnlyList<string> named)
    {
        if (named.Count == 0)
        {
            return LuxembourgAknInFileIdentityClass.TemplateEmpty;
        }

        if (named.Contains(root, StringComparer.Ordinal))
        {
            return LuxembourgAknInFileIdentityClass.Own;
        }

        var work = LuxembourgEliState.WorkOf(root);
        var same = 0;
        var other = 0;
        var sameAreActs = true;
        var otherIsTcOrTrep = false;
        foreach (var state in named)
        {
            if (work is not null &&
                string.Equals(LuxembourgEliState.WorkOf(state), work, StringComparison.Ordinal))
            {
                same++;
                sameAreActs &= LuxembourgEliState.IsAct(state);
            }
            else
            {
                other++;
                otherIsTcOrTrep |= state.Contains("/eli/etat/leg/tc/", StringComparison.Ordinal) ||
                    state.Contains("/eli/etat/leg/trep/", StringComparison.Ordinal);
            }
        }

        if (same != 0 && other == 0)
        {
            return sameAreActs
                ? LuxembourgAknInFileIdentityClass.SameWorkAct
                : LuxembourgAknInFileIdentityClass.SameWorkOtherState;
        }

        return otherIsTcOrTrep
            ? LuxembourgAknInFileIdentityClass.TcOrTrep
            : LuxembourgAknInFileIdentityClass.OtherWork;
    }

    /// <summary>
    /// The instrument a title designates, as <c>type|yyyy-MM-dd</c>: white space runs as one space,
    /// invariant lower case, the leftmost match of <see cref="TitleDesignationPattern"/>, and an exact
    /// date; otherwise null.
    /// </summary>
    private static string? TitleDesignation(string title)
    {
        var normalized = Regex.Replace(title, "\\s+", " ").ToLowerInvariant();
        var match = Regex.Match(normalized, TitleDesignationPattern);
        if (!match.Success)
        {
            return null;
        }

        var type = match.Groups[1].Value;
        var code = type == "loi" ? "loi"
            : type.EndsWith("grand-ducal", StringComparison.Ordinal) ? type[0] == 'r' ? "rgd" : "agd"
            : type[0] == 'r' ? "rmin" : "amin";
        var dayText = match.Groups[2].Value;
        var day = dayText.Replace(" ", "", StringComparison.Ordinal) == "1er"
            ? 1
            : int.Parse(dayText, NumberStyles.None, CultureInfo.InvariantCulture);
        var month = match.Groups[3].Value switch
        {
            "janvier" => 1,
            "f\u00E9vrier" or "fevrier" => 2,
            "mars" => 3,
            "avril" => 4,
            "mai" => 5,
            "juin" => 6,
            "juillet" => 7,
            "ao\u00FBt" or "aout" => 8,
            "septembre" => 9,
            "octobre" => 10,
            "novembre" => 11,
            "d\u00E9cembre" or "decembre" => 12,
            _ => 0,
        };
        var date = match.Groups[4].Value + "-" +
            month.ToString("00", CultureInfo.InvariantCulture) + "-" +
            day.ToString("00", CultureInfo.InvariantCulture);
        return ExactCivilDate(date) ? code + "|" + date : null;
    }

    /// <summary>The instrument a root's ELI designates, as <c>type|yyyy-MM-dd</c>, or null (a code).</summary>
    private static string? EliDesignation(string root)
    {
        var match = Regex.Match(root + "/", "/eli/etat/leg/([a-z]+)/([0-9]{4})/([0-9]{2})/([0-9]{2})/");
        return match.Success
            ? match.Groups[1].Value + "|" + match.Groups[2].Value + "-" + match.Groups[3].Value + "-" +
                match.Groups[4].Value
            : null;
    }

    /// <summary>
    /// The wording comparator of checks (e) and (f): the main-body articles' ids and texts, then the
    /// annex containers' texts (the document element's <c>attachments</c> and <c>components</c>
    /// children, and any <c>components</c> beside it).
    /// </summary>
    private static string Wording(
        XElement root,
        XElement documentElement,
        IReadOnlyList<XElement> articles,
        IReadOnlyList<string> ids)
    {
        XNamespace akn = AknNamespace;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        LuxembourgAknArticleInventory.Append(hash, "lex-v3-luxembourg-akn-inventory-wording/1");
        LuxembourgAknArticleInventory.Append(hash, articles.Count.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < articles.Count; index++)
        {
            LuxembourgAknArticleInventory.Append(hash, ids[index]);
            LuxembourgAknArticleInventory.Append(hash, Text(articles[index]));
        }

        var containers = new List<XElement>();
        foreach (var child in root.Elements())
        {
            if (ReferenceEquals(child, documentElement))
            {
                foreach (var part in documentElement.Elements())
                {
                    if (part.Name == akn + "attachments" || part.Name == akn + "components")
                    {
                        containers.Add(part);
                    }
                }
            }
            else if (child.Name == akn + "components")
            {
                containers.Add(child);
            }
        }

        LuxembourgAknArticleInventory.Append(hash, containers.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var container in containers)
        {
            LuxembourgAknArticleInventory.Append(hash, Text(container));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// An element's text: each maximal run of adjacent character data (text and CDATA) is one part,
    /// any element or processing instruction or the element's end closes a run, child elements are
    /// read except those in the <c>scl</c> namespace, the parts are joined by one space, runs of XML
    /// white space become one space and the ends are trimmed. No Unicode normalization.
    /// </summary>
    private static string Text(XElement element)
    {
        XNamespace scl = SclNamespace;
        var parts = new List<string>();
        var run = new StringBuilder();
        var resume = new Stack<XNode?>();
        var node = element.FirstNode;
        while (true)
        {
            if (node is null)
            {
                Close(run, parts);
                if (resume.Count == 0)
                {
                    break;
                }
                node = resume.Pop();
                continue;
            }

            if (node is XText text)
            {
                run.Append(text.Value);
                node = node.NextNode;
                continue;
            }

            Close(run, parts);
            if (node is XElement child && child.Name.Namespace != scl)
            {
                resume.Push(node.NextNode);
                node = child.FirstNode;
                continue;
            }
            node = node.NextNode;
        }

        var joined = string.Join(' ', parts);
        var collapsed = new StringBuilder(joined.Length);
        var gap = false;
        foreach (var character in joined)
        {
            if (character is ' ' or '\t' or '\n' or '\r')
            {
                gap = true;
                continue;
            }

            if (gap && collapsed.Length != 0)
            {
                collapsed.Append(' ');
            }
            gap = false;
            collapsed.Append(character);
        }
        return collapsed.ToString();
    }

    private static void Close(StringBuilder run, List<string> parts)
    {
        if (run.Length != 0)
        {
            parts.Add(run.ToString());
            run.Clear();
        }
    }

    /// <summary>
    /// The publisher-token rule for ids and wIds: 1 to 512 UTF-16 code units, any scalar except
    /// Unicode White_Space, controls, the zero-width set and the bidi controls, never normalized.
    /// </summary>
    private static bool PublisherToken(string? value)
    {
        if (value is null || value.Length is 0 or > MaximumPublisherTokenLength)
        {
            return false;
        }

        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is <= 0x20 or (>= 0x7F and <= 0xA0) or 0x061C or 0x1680 or
                (>= 0x2000 and <= 0x200F) or (>= 0x2028 and <= 0x202F) or
                (>= 0x205F and <= 0x2060) or (>= 0x2066 and <= 0x2069) or 0x3000 or 0xFEFF)
            {
                return false;
            }
        }
        return true;
    }

    private static bool ExactCivilDate(string value) =>
        DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) &&
        string.Equals(
            value, parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static Dictionary<string, List<(string Start, string State)>> StartsByWork(
        IReadOnlyDictionary<string, string> catalogueStarts)
    {
        var byWork = new Dictionary<string, List<(string Start, string State)>>(StringComparer.Ordinal);
        foreach (var (state, start) in catalogueStarts)
        {
            if (!LuxembourgEliState.IsConsolidationOrCode(state) || LuxembourgEliState.WorkOf(state) is not { } work)
            {
                continue;
            }

            if (!byWork.TryGetValue(work, out var starts))
            {
                starts = [];
                byWork.Add(work, starts);
            }
            starts.Add((start, state));
        }

        foreach (var starts in byWork.Values)
        {
            starts.Sort(static (left, right) =>
            {
                var byStart = string.CompareOrdinal(left.Start, right.Start);
                return byStart != 0 ? byStart : string.CompareOrdinal(left.State, right.State);
            });
        }
        return byWork;
    }

    private static bool HasEarlierConsolidation(List<(string Start, string State)>? workStarts, string start)
    {
        if (workStarts is null)
        {
            return false;
        }

        foreach (var (candidateStart, candidateState) in workStarts)
        {
            if (string.CompareOrdinal(candidateStart, start) < 0 &&
                LuxembourgEliState.IsConsolidation(candidateState))
            {
                return true;
            }
        }
        return false;
    }

    private static bool AnySameWording(List<int> holders, string?[] wordings, string wording)
    {
        foreach (var holder in holders)
        {
            if (string.Equals(wordings[holder], wording, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void Add(Dictionary<string, List<int>> index, string key, int value)
    {
        if (!index.TryGetValue(key, out var values))
        {
            values = [];
            index.Add(key, values);
        }
        values.Add(value);
    }

    private static string TokenName(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
        LuxembourgUserFormatToken.Xml => "xml",
        LuxembourgUserFormatToken.PdfA => "pdfa",
        LuxembourgUserFormatToken.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };
}
