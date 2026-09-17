using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Contracts.Derivation;

/// <summary>Why <see cref="EuCorrigendumTripwireSet.TryDerive"/> derived nothing. Closed.</summary>
/// <remarks>
/// One member per named condition this door can refuse on, and no member for a row it simply does
/// not read. Family P legitimately delivers rows about every predicate of every object it was asked
/// about; only the <c>resource_legal_corrects_resource_legal</c> rows are this door's subject, and a
/// row outside that subject is out of scope, not dropped. A row inside it that disagrees with its
/// own promised shape refuses here by name.
/// </remarks>
public enum EuCorrigendumTripwireRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The object-facts delivery would not reopen through
    /// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>. The door's own reason travels in the
    /// detail; nothing is read from rows that did not survive it.
    /// </summary>
    [JsonStringEnumMemberName("object_facts_rows_refused")]
    ObjectFactsRowsRefused = 1,

    /// <summary>
    /// An object-facts page's custody receipt does not name the bytes that page carried, so no edge
    /// read from it can cite where it came from. The same check, for the same reason, as
    /// <see cref="EuLanguageScopedExpressionDecodeRefusal.PageReceiptDoesNotBindItsBytes"/>.
    /// </summary>
    [JsonStringEnumMemberName("page_receipt_does_not_bind_its_bytes")]
    PageReceiptDoesNotBindItsBytes = 2,

    /// <summary>
    /// Which page carried which row cannot be established for this delivery, so no edge can honestly
    /// cite its bytes. Same rule as
    /// <see cref="EuLanguageScopedExpressionDecodeRefusal.PageAttributionUnavailable"/>.
    /// </summary>
    [JsonStringEnumMemberName("page_attribution_unavailable")]
    PageAttributionUnavailable = 3,

    /// <summary>
    /// A Corrects row's subject was not a canonicalizable Work root, its <c>value_kind</c> was not a
    /// plain literal agreeing with its value, its bound value was not an IRI, or its target would
    /// not canonicalize. The offending IRI travels beside the refusal.
    /// </summary>
    [JsonStringEnumMemberName("corrects_row_term_kind_mismatch")]
    CorrectsRowTermKindMismatch = 4,

    /// <summary>
    /// One Work carried both the unbound "corrects nothing" marker and a bound Corrects edge. The
    /// object decoder refuses the same pairing as a malformed delivery; choosing either reading
    /// silently would publish a fact the publisher did not state.
    /// </summary>
    [JsonStringEnumMemberName("corrects_unbound_marker_beside_edges")]
    CorrectsUnboundMarkerBesideEdges = 5,

    /// <summary>
    /// <see cref="EuLanguageScopedExpressionDerivation.TryDerive"/> refused the same two deliveries.
    /// Its own refusal and the decoder's travel in the detail rather than being flattened into this
    /// one; this door adds no vocabulary for conditions those doors already name exactly.
    /// </summary>
    [JsonStringEnumMemberName("expression_derivation_refused")]
    ExpressionDerivationRefused = 6,
}

/// <summary>
/// Whether a corrigendum expression's language is one the reviewed scope serves bodies in.
/// </summary>
/// <remarks>
/// THIS IS THE TRIPWIRE'S WHOLE POINT. The reviewed scope fetches bodies in exactly
/// <see cref="EuLanguageBodyDisposition.BodyCandidateLanguages"/> (English and French), and the
/// measured defect B31-L0071 names is that corrigenda exist in languages outside that set - a reader
/// of the English text has no body in which the correction can be read. The reach is stated per
/// expression and never summarized into "has a corrigendum", because "has a corrigendum you can
/// read" and "has a corrigendum you cannot" are the two facts a display must keep apart.
/// </remarks>
public enum EuCorrigendumLanguageReach
{
    /// <summary>The expression's language is one the reviewed scope serves bodies in.</summary>
    [JsonStringEnumMemberName("within_served_body_languages")]
    WithinServedBodyLanguages = 1,

    /// <summary>
    /// The expression's language is not one the reviewed scope serves bodies in. The corrigendum
    /// exists; no served body carries it.
    /// </summary>
    [JsonStringEnumMemberName("outside_served_body_languages")]
    OutsideServedBodyLanguages = 2,
}

/// <summary>What the consulted delivery stated about a corrigendum expression's publisher date.</summary>
/// <remarks>
/// <para>
/// TWO STATES, BECAUSE THIS DOOR ALWAYS CONSULTS THE DATE DELIVERY. The object-facts delivery is
/// where the Corrects edges come from, so it is present in every call, and "no date delivery was
/// consulted" is a state no path here can produce. A closed vocabulary with a member nothing mints
/// would be an unreachable claim, so there is none.
/// </para>
/// <para>
/// THE SECOND STATE IS EXACT BY CONSTRUCTION, NOT BY ASSUMPTION. A line exists only for a work the
/// consulted delivery stated a Corrects edge for, and family P states a work's rows only for a work
/// it was asked about. So for every line the delivery did ask about the work, and an expression
/// without a date is that delivery's own statement that it holds none - not a work the batch never
/// named. Review of an earlier head found a "consulted" state that was true of the delivery as a
/// whole and not of the work; this one is true of the work.
/// </para>
/// </remarks>
public enum EuCorrigendumDateState
{
    /// <summary>The publisher stated a date, carried verbatim beside this state.</summary>
    [JsonStringEnumMemberName("publisher_dated")]
    PublisherDated = 1,

    /// <summary>The consulted delivery, which asked about this work, stated no date for it.</summary>
    [JsonStringEnumMemberName("not_stated_by_consulted_delivery")]
    NotStatedByConsultedDelivery = 2,
}

/// <summary>Why one work the derivation holds expressions of could not be placed. Closed.</summary>
public enum EuCorrigendumTripwireGapReason
{
    /// <summary>
    /// The consulted delivery carried neither a Corrects edge nor the unbound "corrects nothing"
    /// marker for this work, so whether it corrects anything is unknown. Decision 64: only a complete
    /// statement supports an absence claim, and this fold makes none for such a work.
    /// </summary>
    [JsonStringEnumMemberName("corrects_not_stated_by_consulted_delivery")]
    CorrectsNotStatedByConsultedDelivery = 1,
}

/// <summary>
/// One corrigendum expression as it bears on one corrected work: the fact the display tripwire
/// renders as "a corrigendum dated X exists; language Y".
/// </summary>
public sealed class EuCorrigendumTripwireLine
{
    private EuCorrigendumTripwireLine(
        string correctedWorkRoot,
        string corrigendumWorkRoot,
        string publisherExpressionId,
        string languageIri,
        EuCorrigendumLanguageReach reach,
        EuCorrigendumDateState dateState,
        PublisherCorrigendumDate? publisherCorrigendumDate,
        string expressionContentSha256,
        ReadOnlyCollection<string> lineageContentSha256InOrder,
        ReadOnlyCollection<string> correctsPageContentSha256InOrder)
    {
        CorrectedWorkRoot = correctedWorkRoot;
        CorrigendumWorkRoot = corrigendumWorkRoot;
        PublisherExpressionId = publisherExpressionId;
        LanguageIri = languageIri;
        Reach = reach;
        DateState = dateState;
        PublisherCorrigendumDate = publisherCorrigendumDate;
        ExpressionContentSha256 = expressionContentSha256;
        LineageContentSha256InOrder = lineageContentSha256InOrder;
        CorrectsPageContentSha256InOrder = correctsPageContentSha256InOrder;
    }

    /// <summary>The canonical root of the work this corrigendum corrects.</summary>
    public string CorrectedWorkRoot { get; }

    /// <summary>The canonical root of the corrigendum work: the expression's publisher work.</summary>
    public string CorrigendumWorkRoot { get; }

    /// <summary>The publisher's own Expression identity within the corrigendum work.</summary>
    public string PublisherExpressionId { get; }

    /// <summary>The publisher's language authority IRI, verbatim. Never mapped onto the scope enum.</summary>
    public string LanguageIri { get; }

    public EuCorrigendumLanguageReach Reach { get; }

    public EuCorrigendumDateState DateState { get; }

    /// <summary>The publisher's date exactly as stated, present only when <see cref="DateState"/> says so.</summary>
    public PublisherCorrigendumDate? PublisherCorrigendumDate { get; }

    /// <summary>
    /// The expression's own <see cref="LanguageScopedExpression.CanonicalContentSha256"/>. Redundant
    /// with the line's own fields on purpose: it binds the line to the expression the derivation
    /// admitted rather than to a re-statement of it.
    /// </summary>
    public string ExpressionContentSha256 { get; }

    /// <summary>The expression's retained transport-byte lineage, by content digest, in its own order.</summary>
    public IReadOnlyList<string> LineageContentSha256InOrder { get; }

    /// <summary>
    /// The retained object-facts page bytes that stated this corrigendum corrects this work, by
    /// content digest, ordinal. Every page that stated the edge, not a representative one - the
    /// discipline the decoder holds for a date stated on more than one page.
    /// </summary>
    public IReadOnlyList<string> CorrectsPageContentSha256InOrder { get; }

    /// <summary>The closed reason token a display renders from.</summary>
    public string ReasonCode => (Reach, DateState) switch
    {
        (EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumDateState.PublisherDated) =>
            "corrigendum_dated_within_served_languages",
        (EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumDateState.NotStatedByConsultedDelivery) =>
            "corrigendum_undated_within_served_languages",
        (EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumDateState.PublisherDated) =>
            "corrigendum_dated_outside_served_languages",
        (EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumDateState.NotStatedByConsultedDelivery) =>
            "corrigendum_undated_outside_served_languages",
        _ => throw new InvalidOperationException("Every reach and date state pair has a reason code."),
    };

    internal static EuCorrigendumTripwireLine Create(
        string correctedWorkRoot,
        LanguageScopedExpression expression,
        IEnumerable<string> correctsPageContentSha256)
    {
        return new EuCorrigendumTripwireLine(
            correctedWorkRoot,
            expression.Identity.PublisherWorkId,
            expression.Identity.PublisherExpressionId,
            expression.OfficialLanguage,
            EuCorrigendumTripwireSet.ReachOf(expression.OfficialLanguage),
            expression.PublisherCorrigendumDate is not null
                ? EuCorrigendumDateState.PublisherDated
                : EuCorrigendumDateState.NotStatedByConsultedDelivery,
            expression.PublisherCorrigendumDate,
            expression.CanonicalContentSha256,
            expression.Lineage.Entries.Select(static entry => entry.ContentSha256).ToList().AsReadOnly(),
            EuCorrigendumTripwireSet.OrdinalSorted(correctsPageContentSha256));
    }
}

/// <summary>
/// A corrigendum work the consulted delivery states corrects a work, for which the derivation
/// holds no expression: the edge is a fact with lineage, the languages are not derived.
/// </summary>
public sealed record EuCorrigendumWithoutDerivedExpressions
{
    internal EuCorrigendumWithoutDerivedExpressions(
        string corrigendumWorkRoot, ReadOnlyCollection<string> correctsPageContentSha256InOrder)
    {
        CorrigendumWorkRoot = corrigendumWorkRoot;
        CorrectsPageContentSha256InOrder = correctsPageContentSha256InOrder;
    }

    public string CorrigendumWorkRoot { get; }

    /// <summary>The retained pages that stated the edge, exactly as a line carries them.</summary>
    public IReadOnlyList<string> CorrectsPageContentSha256InOrder { get; }
}

/// <summary>One work the derivation holds expressions of that this fold could not place, and why.</summary>
public sealed record EuCorrigendumTripwireUnresolvedGap
{
    internal EuCorrigendumTripwireUnresolvedGap(string workRoot, EuCorrigendumTripwireGapReason reason)
    {
        WorkRoot = workRoot;
        Reason = reason;
    }

    public string WorkRoot { get; }

    public EuCorrigendumTripwireGapReason Reason { get; }
}

/// <summary>The tripwire for one corrected work: every corrigendum expression this fold holds for it.</summary>
public sealed class EuCorrigendumTripwire
{
    private const string TripwireSchema = "eu_corrigendum_tripwire/1";

    public static string Schema => TripwireSchema;

    private EuCorrigendumTripwire(
        string correctedWorkRoot,
        ReadOnlyCollection<EuCorrigendumTripwireLine> lines,
        ReadOnlyCollection<EuCorrigendumWithoutDerivedExpressions> corrigendaWithoutDerivedExpressions)
    {
        CorrectedWorkRoot = correctedWorkRoot;
        Lines = lines;
        CorrigendaWithoutDerivedExpressions = corrigendaWithoutDerivedExpressions;
        TripwireSha256 = CanonicalSha256Of(CanonicalTripwireDocument.Of(this));
    }

    public string CorrectedWorkRoot { get; }

    /// <summary>Ordinal by corrigendum work root, then by publisher expression identity.</summary>
    public IReadOnlyList<EuCorrigendumTripwireLine> Lines { get; }

    /// <summary>
    /// Corrigendum works whose Corrects edges to this work the consulted delivery stated but for
    /// which the derivation holds no expression, ordinal by root. Stated, not dropped: a display
    /// that omitted them would say "no corrigendum" about a work the publisher says is corrected.
    /// </summary>
    public IReadOnlyList<EuCorrigendumWithoutDerivedExpressions> CorrigendaWithoutDerivedExpressions { get; }

    public int WithinServedCount =>
        Lines.Count(static line => line.Reach == EuCorrigendumLanguageReach.WithinServedBodyLanguages);

    public int OutsideServedCount =>
        Lines.Count(static line => line.Reach == EuCorrigendumLanguageReach.OutsideServedBodyLanguages);

    public int DatedCount =>
        Lines.Count(static line => line.DateState == EuCorrigendumDateState.PublisherDated);

    /// <summary>
    /// A byte-stable identity over this work's tripwire content. Two independent executions that
    /// observed the same publisher statements agree; page digests and run identities are lineage,
    /// held by the set, and are not in it.
    /// </summary>
    public string TripwireSha256 { get; }

    public string Describe() =>
        $"corrected={CorrectedWorkRoot} lines={Lines.Count} (within_served={WithinServedCount} " +
        $"outside_served={OutsideServedCount} dated={DatedCount}) " +
        $"corrigenda_without_derived_expressions={CorrigendaWithoutDerivedExpressions.Count}";

    internal static EuCorrigendumTripwire Create(
        string correctedWorkRoot,
        IEnumerable<EuCorrigendumTripwireLine> lines,
        IEnumerable<EuCorrigendumWithoutDerivedExpressions> corrigendaWithoutDerivedExpressions)
    {
        var ordered = lines
            .OrderBy(static line => line.CorrigendumWorkRoot, StringComparer.Ordinal)
            .ThenBy(static line => line.PublisherExpressionId, StringComparer.Ordinal)
            .ToList();
        var undecoded = corrigendaWithoutDerivedExpressions
            .OrderBy(static corrigendum => corrigendum.CorrigendumWorkRoot, StringComparer.Ordinal)
            .ToList();
        return new EuCorrigendumTripwire(correctedWorkRoot, ordered.AsReadOnly(), undecoded.AsReadOnly());
    }

    public static string CanonicalSha256Of(CanonicalTripwireDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Lines);
        ArgumentNullException.ThrowIfNull(document.CorrigendaWithoutDerivedExpressions);
        if (!string.Equals(document.Schema, TripwireSchema, StringComparison.Ordinal))
        {
            throw new ArgumentException("The canonical tripwire document has the wrong schema.", nameof(document));
        }

        return Convert.ToHexStringLower(SHA256.HashData(CanonicalBytesOf(document)));
    }

    internal static byte[] CanonicalBytesOf(CanonicalTripwireDocument document) =>
        ContractCanonicalizer.Canonicalize(
            document,
            TripwireSchema + "-canonical-json",
            64);

    public sealed record CanonicalLineDocument(
        string CorrigendumWorkRoot,
        string PublisherExpressionId,
        string LanguageIri,
        string Reach,
        string DateState,
        string? PublisherDateRawLexical,
        string? PublisherDateDatatypeIri,
        string ExpressionContentSha256)
    {
        public static CanonicalLineDocument Of(EuCorrigendumTripwireLine line) =>
            new(
                line.CorrigendumWorkRoot,
                line.PublisherExpressionId,
                line.LanguageIri,
                line.Reach.ToString(),
                line.DateState.ToString(),
                line.PublisherCorrigendumDate?.RawLexical,
                line.PublisherCorrigendumDate?.DatatypeIri,
                line.ExpressionContentSha256);
    }

    public sealed record CanonicalTripwireDocument(
        string Schema,
        string CorrectedWorkRoot,
        IReadOnlyList<CanonicalLineDocument> Lines,
        IReadOnlyList<string> CorrigendaWithoutDerivedExpressions)
    {
        public static CanonicalTripwireDocument Of(EuCorrigendumTripwire tripwire) =>
            new(
                TripwireSchema,
                tripwire.CorrectedWorkRoot,
                [.. tripwire.Lines.Select(CanonicalLineDocument.Of)],
                [.. tripwire.CorrigendaWithoutDerivedExpressions.Select(static corrigendum => corrigendum.CorrigendumWorkRoot)]);
    }
}

/// <summary>
/// The Stage 3 half of B31-L0071's "render the tripwire": from the two proof-bound publisher
/// deliveries the expression derivation already takes, every corrected work's corrigendum tripwire,
/// canonical and byte-stable, with its transport-byte lineage held beside it.
/// </summary>
/// <remarks>
/// <para>
/// WHAT A TRIPWIRE IS HERE. The product specification names the display: "a corrigendum dated X
/// exists; languages Y". Stage 5 renders that sentence; this fold derives the typed fact it renders
/// from - per corrected work, one line per corrigendum expression the derivation holds, carrying the
/// publisher's language verbatim, whether that language is one the reviewed scope serves bodies in,
/// and what the consulted delivery stated about the publisher's date.
/// </para>
/// <para>
/// THE LINK IS THE PUBLISHER'S OWN ROW, READ FROM THE BYTES THAT STATED IT. A corrigendum bears on a
/// work because the publisher states <c>resource_legal_corrects_resource_legal</c> from the
/// corrigendum work to it, and that statement is read here from the object-facts delivery's own
/// retained pages, reopened through <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/> and
/// attributed row by row to the page that carried it. It is NOT read from a decoded snapshot: a
/// snapshot's edge cites the interpretation-profile reference, not the pages, so a derivation from
/// one delivery could have been paired with snapshots from another and nothing would have refused.
/// Review named that shape before freeze and this door has no parameter through which it can be
/// written - the expressions and the edges come from the same two deliveries in one call. A row
/// stating that a work corrects itself is recorded exactly as stated; this fold does not judge
/// publisher data, it carries it.
/// </para>
/// <para>
/// WHAT IS INSIDE THE SUBJECT SET AND WHAT IS NOT. A work with a bound Corrects row is a corrigendum
/// work; its expressions in the derivation are lines on each work it corrects, and if the
/// derivation holds none of its expressions it is listed on each such work as a corrigendum whose
/// languages were not derived - with the pages that stated the edge, because that listing is a
/// claim and a claim carries its lineage. A work with the unbound "corrects nothing" marker is a
/// base act, outside the subject set: not a line, not a gap. A work the derivation holds
/// expressions of, for which the consulted delivery states neither, is an unresolved gap - this
/// fold never says "no corrigendum" for it. Nothing here claims to hold every corrigendum of any
/// work; the scope is exactly the two deliveries handed in.
/// </para>
/// <para>
/// TWO DIGESTS, THE SPLIT <see cref="EuLanguageScopedExpressionDerivation"/> ALREADY MADE. The
/// canonical bytes cover content only - roots, expression identities, languages, reach, date states
/// and dates - ordinal-sorted, so two independent executions over the same publisher statements
/// produce one address. The lineage bytes cover the derivation's stable and episode digests and,
/// per line and per listed corrigendum, the expression's page digests and the Corrects pages'
/// digests, which vary per run and per paging and are what lineage is for.
/// </para>
/// </remarks>
public sealed class EuCorrigendumTripwireSet
{
    private const string SetSchema = "eu_corrigendum_tripwire_set/1";
    private const string LineageSchema = "eu_corrigendum_tripwire_lineage/1";
    private const string LanguageAuthorityBase = "http://publications.europa.eu/resource/authority/language/";

    /// <summary>
    /// The served body languages by the publisher's own authority IRI, spelled from
    /// <see cref="EuLanguageBodyDisposition.BodyCandidateLanguages"/> and from nothing else.
    /// </summary>
    /// <remarks>
    /// FAIL CLOSED ON A POLICY THE TABLE CANNOT NAME. <see cref="SpellServedBodyLanguages"/> spells
    /// the IRI of each served language; if the reviewed policy ever admits a language it does not
    /// spell, this type refuses to load rather than classifying that language's corrigenda as
    /// unreadable. A widened policy with a silently stale table would misstate reach for exactly
    /// the language just admitted.
    /// </remarks>
    public static IReadOnlyList<string> ServedBodyLanguageIris { get; } =
        SpellServedBodyLanguages(EuLanguageBodyDisposition.BodyCandidateLanguages);

    private readonly IReadOnlyDictionary<string, EuCorrigendumTripwire> _byCorrectedWorkRoot;

    private EuCorrigendumTripwireSet(
        EuLanguageScopedExpressionDerivation derivation,
        ReadOnlyCollection<EuCorrigendumTripwire> tripwires,
        ReadOnlyCollection<EuCorrigendumTripwireUnresolvedGap> unresolvedGaps,
        byte[] canonicalBytes,
        byte[] lineageBytes)
    {
        Derivation = derivation;
        Tripwires = tripwires;
        UnresolvedGaps = unresolvedGaps;
        _byCorrectedWorkRoot = tripwires.ToDictionary(
            static tripwire => tripwire.CorrectedWorkRoot, StringComparer.Ordinal);
        CanonicalBytes = canonicalBytes;
        CanonicalSha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
        LineageBytes = lineageBytes;
        LineageSha256 = Convert.ToHexStringLower(SHA256.HashData(lineageBytes));
    }

    public static string Schema => SetSchema;

    public static string LineageRecordSchema => LineageSchema;

    public static string CanonicalSha256Of(CanonicalSetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Tripwires);
        ArgumentNullException.ThrowIfNull(document.UnresolvedGaps);
        if (!string.Equals(document.Schema, SetSchema, StringComparison.Ordinal) ||
            document.Tripwires.Any(static tripwire =>
                tripwire is null ||
                !string.Equals(tripwire.Schema, EuCorrigendumTripwire.Schema, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The canonical corrigendum set document has the wrong schema.", nameof(document));
        }

        return Convert.ToHexStringLower(SHA256.HashData(CanonicalBytesOf(document)));
    }

    private static byte[] CanonicalBytesOf(CanonicalSetDocument document) =>
        ContractCanonicalizer.Canonicalize(
            document,
            SetSchema + "-canonical-json",
            64);

    /// <summary>
    /// The expression derivation minted from the same two deliveries in the same call. Exposed so a
    /// producer retains it once rather than deriving twice; it is not an input.
    /// </summary>
    public EuLanguageScopedExpressionDerivation Derivation { get; }

    /// <summary>The <see cref="EuLanguageScopedExpressionDerivation.DerivationSha256"/> this set was folded with.</summary>
    public string DerivationSha256 => Derivation.DerivationSha256;

    /// <summary>One per corrected work, ordinal by root.</summary>
    public IReadOnlyList<EuCorrigendumTripwire> Tripwires { get; }

    /// <summary>Ordinal by work root.</summary>
    public IReadOnlyList<EuCorrigendumTripwireUnresolvedGap> UnresolvedGaps { get; }

    /// <summary>WHAT WAS DERIVED, canonicalized. Byte-identical across independent executions.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes { get; }

    public string CanonicalSha256 { get; }

    /// <summary>WHERE IT CAME FROM, canonicalized: derivation, episode and page digests. Per run.</summary>
    public ReadOnlyMemory<byte> LineageBytes { get; }

    public string LineageSha256 { get; }

    public EuCorrigendumTripwire? TripwireFor(string correctedWorkRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctedWorkRoot);
        return _byCorrectedWorkRoot.GetValueOrDefault(correctedWorkRoot);
    }

    public string Describe() =>
        $"corrected_works={Tripwires.Count} lines={Tripwires.Sum(static tripwire => tripwire.Lines.Count)} " +
        $"(within_served={Tripwires.Sum(static tripwire => tripwire.WithinServedCount)} " +
        $"outside_served={Tripwires.Sum(static tripwire => tripwire.OutsideServedCount)} " +
        $"dated={Tripwires.Sum(static tripwire => tripwire.DatedCount)}) " +
        $"corrigenda_without_derived_expressions={Tripwires.Sum(static tripwire => tripwire.CorrigendaWithoutDerivedExpressions.Count)} " +
        $"unresolved_gaps={UnresolvedGaps.Count}";

    /// <summary>The reach of one publisher language IRI under the reviewed body policy.</summary>
    public static EuCorrigendumLanguageReach ReachOf(string languageIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageIri);
        return ServedBodyLanguageIris.Contains(languageIri, StringComparer.Ordinal)
            ? EuCorrigendumLanguageReach.WithinServedBodyLanguages
            : EuCorrigendumLanguageReach.OutsideServedBodyLanguages;
    }

    /// <summary>
    /// Folds the two deliveries into every corrected work's tripwire, or refuses without folding.
    /// </summary>
    /// <param name="expressionFacts">
    /// The family X (Expression-facts) delivery, in the proof-bound shape the decoder requires.
    /// </param>
    /// <param name="objectFacts">
    /// The family P (object-facts) delivery, in the same shape. Required: it is where the Corrects
    /// edges and the corrigendum dates are read from, reopened here and never accepted as rows.
    /// </param>
    /// <param name="refusal">This door's own refusal, or <see cref="EuCorrigendumTripwireRefusal.None"/>.</param>
    /// <param name="detail">The inner door's reason, or the offenders, when the refusal has one.</param>
    /// <param name="offendingIri">The IRI a refusal is about, when it has one.</param>
    public static EuCorrigendumTripwireSet? TryDerive(
        EuProofBoundDelivery expressionFacts,
        EuProofBoundDelivery objectFacts,
        out EuCorrigendumTripwireRefusal refusal,
        out string? detail,
        out string? offendingIri)
    {
        ArgumentNullException.ThrowIfNull(expressionFacts);
        ArgumentNullException.ThrowIfNull(objectFacts);
        refusal = EuCorrigendumTripwireRefusal.None;
        detail = null;
        offendingIri = null;

        // ---- The object-facts delivery, reopened through the one door. Rows are never accepted. ----
        var objectProfile = objectFacts.Profile;
        var objectRows = VerifiedRepeatedEnumerationRows.TryOpen(
            objectFacts.Proof,
            objectFacts.Comparison,
            objectProfile,
            objectFacts.ProfileRef,
            objectFacts.CountHttpEvidenceRef,
            objectFacts.PagesInOrder,
            out var openRefusal);
        if (objectRows is null)
        {
            refusal = EuCorrigendumTripwireRefusal.ObjectFactsRowsRefused;
            detail = openRefusal.ToString();
            return null;
        }

        // Rows first, then the provenance about those rows - the decoder's order, for its reason.
        if (!EuLanguageScopedExpressionDecode.EveryPageReceiptBindsItsBytes(objectFacts, out offendingIri))
        {
            refusal = EuCorrigendumTripwireRefusal.PageReceiptDoesNotBindItsBytes;
            return null;
        }

        var pageOfRow = EuLanguageScopedExpressionDecode.PageOfEachRow(objectFacts, objectRows.Count);
        if (pageOfRow is null)
        {
            refusal = EuCorrigendumTripwireRefusal.PageAttributionUnavailable;
            return null;
        }

        // ---- The Corrects rows: corrigendum root -> corrected root -> the pages that stated it. ----
        var correctsIri = EuObjectFactsDiscoveryPlan.RelationIri(EuRelationFamily.Corrects);
        var pagesByEdge = new Dictionary<string, Dictionary<string, SortedSet<int>>>(StringComparer.Ordinal);
        var statedNoCorrection = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < objectRows.Count; index++)
        {
            var row = objectRows[index];
            var predicateTerm = EuLanguageScopedExpressionDecode.Term(row, objectProfile, "predicate");
            if (predicateTerm.Kind != RepeatedEnumerationRdfTermKind.Iri ||
                !string.Equals(predicateTerm.Value, correctsIri, StringComparison.Ordinal))
            {
                // Outside this door's subject set: another door's row, refused there if malformed.
                continue;
            }

            var objectTerm = EuLanguageScopedExpressionDecode.Term(row, objectProfile, "object");
            var valueTerm = EuLanguageScopedExpressionDecode.Term(row, objectProfile, "value");
            var valueKindTerm = EuLanguageScopedExpressionDecode.Term(row, objectProfile, "value_kind");
            if (objectTerm.Kind != RepeatedEnumerationRdfTermKind.Iri || objectTerm.Value is null ||
                !EuLanguageScopedExpressionDecode.IsPlainLiteral(valueKindTerm) ||
                (valueTerm.Kind == RepeatedEnumerationRdfTermKind.Unbound) != (valueKindTerm.Value == "unbound"))
            {
                refusal = EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch;
                offendingIri = objectTerm.Value;
                return null;
            }

            var corrigendum = EuPackRootCanonicalForm.TryCanonicalize(objectTerm.Value, out _);
            if (corrigendum is null)
            {
                refusal = EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch;
                offendingIri = objectTerm.Value;
                return null;
            }

            if (valueKindTerm.Value == "unbound")
            {
                statedNoCorrection.Add(corrigendum);
                continue;
            }

            if (valueKindTerm.Value != "iri" || valueTerm.Kind != RepeatedEnumerationRdfTermKind.Iri ||
                valueTerm.Value is null)
            {
                refusal = EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch;
                offendingIri = corrigendum;
                return null;
            }

            var corrected = EuPackRootCanonicalForm.TryCanonicalize(valueTerm.Value, out _);
            if (corrected is null)
            {
                refusal = EuCorrigendumTripwireRefusal.CorrectsRowTermKindMismatch;
                offendingIri = valueTerm.Value;
                return null;
            }

            if (!pagesByEdge.TryGetValue(corrigendum, out var targets))
            {
                targets = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
                pagesByEdge.Add(corrigendum, targets);
            }

            if (!targets.TryGetValue(corrected, out var pages))
            {
                pages = [];
                targets.Add(corrected, pages);
            }

            pages.Add(pageOfRow[index]);
        }

        var contradictory = statedNoCorrection
            .Where(pagesByEdge.ContainsKey)
            .OrderBy(static root => root, StringComparer.Ordinal)
            .ToArray();
        if (contradictory.Length > 0)
        {
            refusal = EuCorrigendumTripwireRefusal.CorrectsUnboundMarkerBesideEdges;
            offendingIri = contradictory[0];
            detail = string.Join("; ", contradictory);
            return null;
        }

        // ---- The expressions, from the same two deliveries, in this same call. ----
        var derivation = EuLanguageScopedExpressionDerivation.TryDerive(
            expressionFacts,
            objectFacts,
            out var derivationRefusal,
            out var decodeRefusal,
            out var decodeDetail,
            out var decodeOffendingIri);
        if (derivation is null)
        {
            refusal = EuCorrigendumTripwireRefusal.ExpressionDerivationRefused;
            detail = $"derivation={derivationRefusal} decode={decodeRefusal} detail={decodeDetail}";
            offendingIri = decodeOffendingIri;
            return null;
        }

        // ---- Lines, listed corrigenda, gaps. ----
        var expressionsByWork = derivation.Expressions
            .GroupBy(static expression => expression.Identity.PublisherWorkId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);
        var gaps = expressionsByWork.Keys
            .Where(work => !pagesByEdge.ContainsKey(work) && !statedNoCorrection.Contains(work))
            .OrderBy(static work => work, StringComparer.Ordinal)
            .Select(static work => new EuCorrigendumTripwireUnresolvedGap(
                work, EuCorrigendumTripwireGapReason.CorrectsNotStatedByConsultedDelivery))
            .ToList();
        var linesByCorrected = new Dictionary<string, List<EuCorrigendumTripwireLine>>(StringComparer.Ordinal);
        var undecodedByCorrected = new Dictionary<string, List<EuCorrigendumWithoutDerivedExpressions>>(StringComparer.Ordinal);
        foreach (var (corrigendum, targets) in pagesByEdge)
        {
            var held = expressionsByWork.GetValueOrDefault(corrigendum);
            foreach (var (corrected, pages) in targets)
            {
                var pageDigests = pages
                    .Select(page => objectFacts.PagesInOrder[page].DurableWriteReceipt.Reference.ContentSha256)
                    .ToArray();
                if (held is null)
                {
                    if (!undecodedByCorrected.TryGetValue(corrected, out var undecoded))
                    {
                        undecoded = [];
                        undecodedByCorrected.Add(corrected, undecoded);
                    }

                    undecoded.Add(new EuCorrigendumWithoutDerivedExpressions(corrigendum, OrdinalSorted(pageDigests)));
                    continue;
                }

                if (!linesByCorrected.TryGetValue(corrected, out var lines))
                {
                    lines = [];
                    linesByCorrected.Add(corrected, lines);
                }

                foreach (var expression in held)
                {
                    lines.Add(EuCorrigendumTripwireLine.Create(corrected, expression, pageDigests));
                }
            }
        }

        var tripwires = linesByCorrected.Keys
            .Union(undecodedByCorrected.Keys, StringComparer.Ordinal)
            .OrderBy(static root => root, StringComparer.Ordinal)
            .Select(root => EuCorrigendumTripwire.Create(
                root,
                linesByCorrected.GetValueOrDefault(root) ?? [],
                undecodedByCorrected.GetValueOrDefault(root) ?? []))
            .ToList();

        var canonicalBytes = CanonicalBytesOf(
            new CanonicalSetDocument(
                SetSchema,
                [.. tripwires.Select(EuCorrigendumTripwire.CanonicalTripwireDocument.Of)],
                [.. gaps.Select(CanonicalGapDocument.Of)]));
        var lineageBytes = ContractCanonicalizer.Canonicalize(
            new CanonicalLineageDocument(
                LineageSchema,
                Convert.ToHexStringLower(SHA256.HashData(canonicalBytes)),
                derivation.DerivationSha256,
                derivation.EpisodeSha256,
                [.. tripwires.SelectMany(static tripwire => tripwire.Lines).Select(CanonicalLineLineageDocument.Of)],
                [.. tripwires.SelectMany(static tripwire =>
                    tripwire.CorrigendaWithoutDerivedExpressions.Select(corrigendum =>
                        CanonicalListedCorrigendumLineageDocument.Of(tripwire.CorrectedWorkRoot, corrigendum)))]),
            LineageSchema + "-canonical-json",
            64);

        return new EuCorrigendumTripwireSet(
            derivation,
            tripwires.AsReadOnly(),
            gaps.AsReadOnly(),
            canonicalBytes,
            lineageBytes);
    }

    /// <summary>
    /// Spells the served body languages by the publisher's authority IRI, ordinal, or refuses a
    /// policy naming a language it cannot spell. Pure, so the refusal is testable without a policy
    /// change.
    /// </summary>
    internal static IReadOnlyList<string> SpellServedBodyLanguages(IEnumerable<EuOfficialLanguage> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var spelled = new Dictionary<EuOfficialLanguage, string>
        {
            [EuOfficialLanguage.English] = LanguageAuthorityBase + "ENG",
            [EuOfficialLanguage.French] = LanguageAuthorityBase + "FRA",
        };
        var iris = new List<string>();
        foreach (var language in policy)
        {
            if (!spelled.TryGetValue(language, out var iri))
            {
                throw new InvalidOperationException(
                    $"The reviewed body policy serves {language}, whose authority IRI this table does not " +
                    "spell; the tripwire's reach cannot be stated for it.");
            }

            iris.Add(iri);
        }

        iris.Sort(StringComparer.Ordinal);
        return iris.AsReadOnly();
    }

    /// <summary>
    /// Ordinal, and nothing else: the page ordinals behind these digests are already a set, and two
    /// pages of one proven delivery cannot carry identical bytes because their rows' canonical keys
    /// are unique across the delivery. A de-duplication here was written, found unreachable by the
    /// lens, and removed rather than kept as a check no test could establish.
    /// </summary>
    internal static ReadOnlyCollection<string> OrdinalSorted(IEnumerable<string> digests) =>
        digests
            .OrderBy(static digest => digest, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

    public sealed record CanonicalGapDocument(string WorkRoot, string Reason)
    {
        public static CanonicalGapDocument Of(EuCorrigendumTripwireUnresolvedGap gap) =>
            new(gap.WorkRoot, gap.Reason.ToString());
    }

    public sealed record CanonicalSetDocument(
        string Schema,
        IReadOnlyList<EuCorrigendumTripwire.CanonicalTripwireDocument> Tripwires,
        IReadOnlyList<CanonicalGapDocument> UnresolvedGaps);

    private sealed record CanonicalLineLineageDocument(
        string CorrectedWorkRoot,
        string CorrigendumWorkRoot,
        string PublisherExpressionId,
        IReadOnlyList<string> LineageContentSha256InOrder,
        IReadOnlyList<string> CorrectsPageContentSha256InOrder)
    {
        public static CanonicalLineLineageDocument Of(EuCorrigendumTripwireLine line) =>
            new(
                line.CorrectedWorkRoot,
                line.CorrigendumWorkRoot,
                line.PublisherExpressionId,
                line.LineageContentSha256InOrder,
                line.CorrectsPageContentSha256InOrder);
    }

    private sealed record CanonicalListedCorrigendumLineageDocument(
        string CorrectedWorkRoot,
        string CorrigendumWorkRoot,
        IReadOnlyList<string> CorrectsPageContentSha256InOrder)
    {
        public static CanonicalListedCorrigendumLineageDocument Of(
            string correctedWorkRoot, EuCorrigendumWithoutDerivedExpressions corrigendum) =>
            new(correctedWorkRoot, corrigendum.CorrigendumWorkRoot, corrigendum.CorrectsPageContentSha256InOrder);
    }

    private sealed record CanonicalLineageDocument(
        string Schema,
        string CanonicalSha256,
        string DerivationSha256,
        string EpisodeSha256,
        IReadOnlyList<CanonicalLineLineageDocument> Lines,
        IReadOnlyList<CanonicalListedCorrigendumLineageDocument> CorrigendaWithoutDerivedExpressions);
}
