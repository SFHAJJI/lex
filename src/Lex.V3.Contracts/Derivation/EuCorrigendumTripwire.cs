using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Contracts.Derivation;

/// <summary>Why <see cref="EuCorrigendumTripwireSet.TryDerive"/> derived nothing. Closed.</summary>
/// <remarks>
/// One member, because this fold has one condition under which its input is not a set of facts it
/// can fold: the same object described twice. Everything else it meets is either a fact it records,
/// a gap it names, or outside its subject set - none of which is a refusal.
/// </remarks>
public enum EuCorrigendumTripwireRefusal
{
    /// <summary>No refusal.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// Two snapshots name one object. One object has one set of Corrects edges; two descriptions of
    /// it are two claims, and this fold does not pick one.
    /// </summary>
    [JsonStringEnumMemberName("snapshot_delivered_twice")]
    SnapshotDeliveredTwice = 1,
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

/// <summary>What is known about a corrigendum expression's publisher date. Three states, never two.</summary>
/// <remarks>
/// <para>
/// THE ABSENCE DOCTRINE, APPLIED TO A DATE. <see cref="EuLanguageScopedExpressionDerivation"/>'s own
/// remarks separate "no date delivery was consulted" from "one was and stated nothing": the first
/// is recorded as a null <see cref="EuLanguageScopedExpressionDerivation.ObjectFactsProof"/>, the
/// second as an expression with no <see cref="PublisherCorrigendumDate"/>. A tripwire that rendered
/// both as "undated" would tell a reader the publisher stated no date when in fact nobody asked.
/// </para>
/// <para>
/// The second state is named for exactly what it claims. The consulted date delivery stated no
/// date for this work; whether it asked about the work is a fact about that delivery's batch, which
/// this contract does not hold and therefore does not assert.
/// </para>
/// </remarks>
public enum EuCorrigendumDateState
{
    /// <summary>The publisher stated a date, carried verbatim beside this state.</summary>
    [JsonStringEnumMemberName("publisher_dated")]
    PublisherDated = 1,

    /// <summary>A date delivery was consulted and stated no date for this work.</summary>
    [JsonStringEnumMemberName("not_stated_by_consulted_delivery")]
    NotStatedByConsultedDelivery = 2,

    /// <summary>No date delivery was consulted, so nothing is known about the date.</summary>
    [JsonStringEnumMemberName("date_delivery_not_consulted")]
    DateDeliveryNotConsulted = 3,
}

/// <summary>Why one snapshot could not contribute to any tripwire. Closed.</summary>
public enum EuCorrigendumTripwireGapReason
{
    /// <summary>
    /// The object's Corrects family is not completely acquired, so whether it corrects anything is
    /// unknown. Decision 64: only a complete bounded observation supports an absence claim, and this
    /// fold makes none for such an object.
    /// </summary>
    [JsonStringEnumMemberName("corrects_not_completely_acquired")]
    CorrectsNotCompletelyAcquired = 1,
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
        ReadOnlyCollection<SourceArtifactRef> correctsEvidenceRefs)
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
        CorrectsEvidenceRefs = correctsEvidenceRefs;
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

    /// <summary>The expression's own <see cref="LanguageScopedExpression.CanonicalContentSha256"/>.</summary>
    public string ExpressionContentSha256 { get; }

    /// <summary>The expression's retained transport-byte lineage, by content digest, in its own order.</summary>
    public IReadOnlyList<string> LineageContentSha256InOrder { get; }

    /// <summary>
    /// Every Corrects edge that states this corrigendum corrects this work, by the evidence that
    /// carried it. Two snapshots of one corrigendum work may each state the edge; both are kept.
    /// </summary>
    public IReadOnlyList<SourceArtifactRef> CorrectsEvidenceRefs { get; }

    /// <summary>The closed reason token a display renders from.</summary>
    public string ReasonCode => (Reach, DateState) switch
    {
        (EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumDateState.PublisherDated) =>
            "corrigendum_dated_within_served_languages",
        (EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumDateState.NotStatedByConsultedDelivery) =>
            "corrigendum_undated_within_served_languages",
        (EuCorrigendumLanguageReach.WithinServedBodyLanguages, EuCorrigendumDateState.DateDeliveryNotConsulted) =>
            "corrigendum_date_unknown_within_served_languages",
        (EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumDateState.PublisherDated) =>
            "corrigendum_dated_outside_served_languages",
        (EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumDateState.NotStatedByConsultedDelivery) =>
            "corrigendum_undated_outside_served_languages",
        (EuCorrigendumLanguageReach.OutsideServedBodyLanguages, EuCorrigendumDateState.DateDeliveryNotConsulted) =>
            "corrigendum_date_unknown_outside_served_languages",
        _ => throw new InvalidOperationException("Every reach and date state pair has a reason code."),
    };

    internal static EuCorrigendumTripwireLine Create(
        string correctedWorkRoot,
        LanguageScopedExpression expression,
        bool dateDeliveryConsulted,
        IEnumerable<SourceArtifactRef> correctsEvidenceRefs)
    {
        var dateState = expression.PublisherCorrigendumDate is not null
            ? EuCorrigendumDateState.PublisherDated
            : dateDeliveryConsulted
                ? EuCorrigendumDateState.NotStatedByConsultedDelivery
                : EuCorrigendumDateState.DateDeliveryNotConsulted;
        var evidence = correctsEvidenceRefs
            .OrderBy(static reference => reference.ResourceId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Sha256, StringComparer.Ordinal)
            .ToList();
        return new EuCorrigendumTripwireLine(
            correctedWorkRoot,
            expression.Identity.PublisherWorkId,
            expression.Identity.PublisherExpressionId,
            expression.OfficialLanguage,
            EuCorrigendumTripwireSet.ReachOf(expression.OfficialLanguage),
            dateState,
            expression.PublisherCorrigendumDate,
            expression.CanonicalContentSha256,
            expression.Lineage.Entries.Select(static entry => entry.ContentSha256).ToList().AsReadOnly(),
            evidence.AsReadOnly());
    }
}

/// <summary>One snapshot this fold could not place, and why.</summary>
public sealed record EuCorrigendumTripwireUnresolvedGap
{
    internal EuCorrigendumTripwireUnresolvedGap(
        string objectPublisherUri,
        string canonicalWorkRoot,
        EuCorrigendumTripwireGapReason reason,
        EuRelationAcquisitionState correctsAcquisition)
    {
        ObjectPublisherUri = objectPublisherUri;
        CanonicalWorkRoot = canonicalWorkRoot;
        Reason = reason;
        CorrectsAcquisition = correctsAcquisition;
    }

    public string ObjectPublisherUri { get; }

    public string CanonicalWorkRoot { get; }

    public EuCorrigendumTripwireGapReason Reason { get; }

    /// <summary>The Corrects family's actual acquisition state, so the gap says how far it got.</summary>
    public EuRelationAcquisitionState CorrectsAcquisition { get; }
}

/// <summary>The tripwire for one corrected work: every corrigendum expression this fold holds for it.</summary>
public sealed class EuCorrigendumTripwire
{
    private const string TripwireSchema = "eu_corrigendum_tripwire/1";

    private EuCorrigendumTripwire(
        string correctedWorkRoot,
        ReadOnlyCollection<EuCorrigendumTripwireLine> lines,
        ReadOnlyCollection<string> corrigendaWithoutDerivedExpressions)
    {
        CorrectedWorkRoot = correctedWorkRoot;
        Lines = lines;
        CorrigendaWithoutDerivedExpressions = corrigendaWithoutDerivedExpressions;
        TripwireSha256 = Convert.ToHexStringLower(
            SHA256.HashData(
                ContractCanonicalizer.Canonicalize(
                    CanonicalTripwireDocument.Of(this),
                    TripwireSchema + "-canonical-json",
                    64)));
    }

    public string CorrectedWorkRoot { get; }

    /// <summary>Ordinal by corrigendum work root, then by publisher expression identity.</summary>
    public IReadOnlyList<EuCorrigendumTripwireLine> Lines { get; }

    /// <summary>
    /// Corrigendum works whose Corrects edges to this work are completely observed but for which the
    /// derivation holds no expression. Stated, not dropped: a display that omitted them would say
    /// "no corrigendum" about a work the publisher says is corrected.
    /// </summary>
    public IReadOnlyList<string> CorrigendaWithoutDerivedExpressions { get; }

    public int WithinServedCount =>
        Lines.Count(static line => line.Reach == EuCorrigendumLanguageReach.WithinServedBodyLanguages);

    public int OutsideServedCount =>
        Lines.Count(static line => line.Reach == EuCorrigendumLanguageReach.OutsideServedBodyLanguages);

    public int DatedCount =>
        Lines.Count(static line => line.DateState == EuCorrigendumDateState.PublisherDated);

    /// <summary>
    /// A byte-stable identity over this work's tripwire content. Two independent executions that
    /// observed the same publisher statements agree; evidence references and run identities are
    /// lineage, held by the set, and are not in it.
    /// </summary>
    public string TripwireSha256 { get; }

    public string Describe() =>
        $"corrected={CorrectedWorkRoot} lines={Lines.Count} (within_served={WithinServedCount} " +
        $"outside_served={OutsideServedCount} dated={DatedCount}) " +
        $"corrigenda_without_derived_expressions={CorrigendaWithoutDerivedExpressions.Count}";

    internal static EuCorrigendumTripwire Create(
        string correctedWorkRoot,
        IEnumerable<EuCorrigendumTripwireLine> lines,
        IEnumerable<string> corrigendaWithoutDerivedExpressions)
    {
        var ordered = lines
            .OrderBy(static line => line.CorrigendumWorkRoot, StringComparer.Ordinal)
            .ThenBy(static line => line.PublisherExpressionId, StringComparer.Ordinal)
            .ToList();
        var undecoded = corrigendaWithoutDerivedExpressions
            .OrderBy(static root => root, StringComparer.Ordinal)
            .ToList();
        return new EuCorrigendumTripwire(correctedWorkRoot, ordered.AsReadOnly(), undecoded.AsReadOnly());
    }

    internal sealed record CanonicalLineDocument(
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

    internal sealed record CanonicalTripwireDocument(
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
                tripwire.CorrigendaWithoutDerivedExpressions);
    }
}

/// <summary>
/// The Stage 3 half of B31-L0071's "render the tripwire": from one retained expression derivation
/// and the decoded object snapshots, every corrected work's corrigendum tripwire, canonical and
/// byte-stable, with its lineage held beside it.
/// </summary>
/// <remarks>
/// <para>
/// WHAT A TRIPWIRE IS HERE. The product specification names the display: "a corrigendum dated X
/// exists; languages Y". Stage 5 renders that sentence; this fold derives the typed fact it renders
/// from - per corrected work, one line per corrigendum expression the derivation holds, carrying the
/// publisher's language verbatim, whether that language is one the reviewed scope serves bodies in,
/// and what is known about the publisher's date.
/// </para>
/// <para>
/// THE LINK IS THE PUBLISHER'S OWN EDGE. A corrigendum bears on a work because the publisher states
/// <c>resource_legal_corrects_resource_legal</c> from the corrigendum work to it, and that statement
/// arrives here as a <see cref="EuRelationFamily.Corrects"/> edge on a snapshot whose Corrects family
/// is <see cref="EuRelationAcquisitionState.Complete"/>. The link is NOT derived from the CELEX
/// <c>R(nn)</c> suffix: that would be an inference the publisher did not state, and the expression
/// identities are cellar roots rather than CELEX numbers in any case.
/// </para>
/// <para>
/// WHAT IS INSIDE THE SUBJECT SET AND WHAT IS NOT. A snapshot with a complete Corrects family and at
/// least one edge is a corrigendum work; its expressions in the derivation are lines on each work it
/// corrects, and if the derivation holds none of its expressions it is listed on each such work as a
/// corrigendum whose languages were not derived. A snapshot whose Corrects family is not complete is
/// an unresolved gap naming the object - this fold never says "no corrigendum" for it. An expression
/// of a work with no Corrects edge - a base act - is outside the subject set: not a line, not a gap,
/// simply not what this fold is about. Nothing here claims to hold every corrigendum of any work; the
/// scope is exactly the snapshots and the derivation handed in.
/// </para>
/// <para>
/// TWO DIGESTS, THE SPLIT <see cref="EuLanguageScopedExpressionDerivation"/> ALREADY MADE. The
/// canonical bytes cover content only - roots, expression identities, languages, reach, date states
/// and dates - ordinal-sorted, so two independent executions over the same publisher statements
/// produce one address. The lineage bytes cover the derivation digest, each expression's content and
/// page digests, and each Corrects edge's evidence reference, which vary per run and are what lineage
/// is for.
/// </para>
/// </remarks>
public sealed class EuCorrigendumTripwireSet
{
    private const string SetSchema = "eu_corrigendum_tripwire_set/1";
    private const string LineageSchema = "eu_corrigendum_tripwire_lineage/1";
    private const string LanguageAuthorityBase = "http://publications.europa.eu/resource/authority/language/";

    /// <summary>
    /// The served body languages by the publisher's own authority IRI, derived from
    /// <see cref="EuLanguageBodyDisposition.BodyCandidateLanguages"/> and from nothing else.
    /// </summary>
    /// <remarks>
    /// FAIL CLOSED ON A POLICY THIS TABLE CANNOT NAME. The table below spells the IRI of each served
    /// language; if the reviewed policy ever admits a language it does not spell, this type refuses
    /// to load rather than classifying that language's corrigenda as unreadable. A widened policy
    /// with a silently stale table would misstate reach for exactly the language just admitted.
    /// </remarks>
    public static IReadOnlyList<string> ServedBodyLanguageIris { get; } = BuildServedBodyLanguageIris();

    private readonly IReadOnlyDictionary<string, EuCorrigendumTripwire> _byCorrectedWorkRoot;

    private EuCorrigendumTripwireSet(
        string derivationSha256,
        ReadOnlyCollection<EuCorrigendumTripwire> tripwires,
        ReadOnlyCollection<EuCorrigendumTripwireUnresolvedGap> unresolvedGaps,
        byte[] canonicalBytes,
        byte[] lineageBytes)
    {
        DerivationSha256 = derivationSha256;
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

    /// <summary>The <see cref="EuLanguageScopedExpressionDerivation.DerivationSha256"/> this set was folded from.</summary>
    public string DerivationSha256 { get; }

    /// <summary>One per corrected work, ordinal by root.</summary>
    public IReadOnlyList<EuCorrigendumTripwire> Tripwires { get; }

    /// <summary>Ordinal by object publisher URI.</summary>
    public IReadOnlyList<EuCorrigendumTripwireUnresolvedGap> UnresolvedGaps { get; }

    /// <summary>WHAT WAS DERIVED, canonicalized. Byte-identical across independent executions.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes { get; }

    public string CanonicalSha256 { get; }

    /// <summary>WHERE IT CAME FROM, canonicalized: derivation, page and evidence references. Per run.</summary>
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

    /// <summary>Folds the derivation and the snapshots into every corrected work's tripwire, or refuses.</summary>
    /// <param name="derivation">The retained expression derivation. Its expressions are the only lines.</param>
    /// <param name="snapshots">
    /// The decoded object snapshots. Their complete Corrects families are the only links. Any
    /// order; the fold is order-independent.
    /// </param>
    public static EuCorrigendumTripwireSet? TryDerive(
        EuLanguageScopedExpressionDerivation derivation,
        IReadOnlyList<EuCellarObjectSnapshot> snapshots,
        out EuCorrigendumTripwireRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(derivation);
        ArgumentNullException.ThrowIfNull(snapshots);
        refusal = EuCorrigendumTripwireRefusal.None;
        detail = null;

        // ---- One description per object, or nothing. Checked over the whole input first. ----
        var seenObjects = new HashSet<string>(StringComparer.Ordinal);
        var twice = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshot, nameof(snapshots));
            if (!seenObjects.Add(snapshot.ObjectRef.PublisherUri))
            {
                twice.Add(snapshot.ObjectRef.PublisherUri);
            }
        }

        if (twice.Count > 0)
        {
            refusal = EuCorrigendumTripwireRefusal.SnapshotDeliveredTwice;
            detail = "two snapshots name one object: " + string.Join("; ", twice);
            return null;
        }

        // ---- The links: corrigendum root -> corrected root -> the evidence that stated it. ----
        var gaps = new List<EuCorrigendumTripwireUnresolvedGap>();
        var edgesByCorrigendum = new Dictionary<string, Dictionary<string, List<SourceArtifactRef>>>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            var corrects = snapshot.Relation(EuRelationFamily.Corrects);
            if (corrects.Acquisition != EuRelationAcquisitionState.Complete)
            {
                gaps.Add(new EuCorrigendumTripwireUnresolvedGap(
                    snapshot.ObjectRef.PublisherUri,
                    snapshot.CanonicalWorkRoot,
                    EuCorrigendumTripwireGapReason.CorrectsNotCompletelyAcquired,
                    corrects.Acquisition));
                continue;
            }

            foreach (var edge in corrects.Edges)
            {
                if (!edgesByCorrigendum.TryGetValue(snapshot.CanonicalWorkRoot, out var targets))
                {
                    targets = new Dictionary<string, List<SourceArtifactRef>>(StringComparer.Ordinal);
                    edgesByCorrigendum.Add(snapshot.CanonicalWorkRoot, targets);
                }

                if (!targets.TryGetValue(edge.TargetWorkRoot, out var evidence))
                {
                    evidence = [];
                    targets.Add(edge.TargetWorkRoot, evidence);
                }

                evidence.Add(edge.EvidenceRef);
            }
        }

        // ---- The lines: every expression of every corrigendum work, on every work it corrects. ----
        var expressionsByWork = derivation.Expressions
            .GroupBy(static expression => expression.Identity.PublisherWorkId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);
        var dateDeliveryConsulted = derivation.ObjectFactsProof is not null;
        var linesByCorrected = new Dictionary<string, List<EuCorrigendumTripwireLine>>(StringComparer.Ordinal);
        var undecodedByCorrected = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (corrigendumRoot, targets) in edgesByCorrigendum)
        {
            var held = expressionsByWork.GetValueOrDefault(corrigendumRoot);
            foreach (var (correctedRoot, evidence) in targets)
            {
                if (held is null)
                {
                    if (!undecodedByCorrected.TryGetValue(correctedRoot, out var undecoded))
                    {
                        undecoded = new SortedSet<string>(StringComparer.Ordinal);
                        undecodedByCorrected.Add(correctedRoot, undecoded);
                    }

                    undecoded.Add(corrigendumRoot);
                    continue;
                }

                if (!linesByCorrected.TryGetValue(correctedRoot, out var lines))
                {
                    lines = [];
                    linesByCorrected.Add(correctedRoot, lines);
                }

                foreach (var expression in held)
                {
                    lines.Add(EuCorrigendumTripwireLine.Create(correctedRoot, expression, dateDeliveryConsulted, evidence));
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
        var orderedGaps = gaps
            .OrderBy(static gap => gap.ObjectPublisherUri, StringComparer.Ordinal)
            .ToList();

        var canonicalBytes = ContractCanonicalizer.Canonicalize(
            new CanonicalSetDocument(
                SetSchema,
                [.. tripwires.Select(EuCorrigendumTripwire.CanonicalTripwireDocument.Of)],
                [.. orderedGaps.Select(CanonicalGapDocument.Of)]),
            SetSchema + "-canonical-json",
            64);
        var lineageBytes = ContractCanonicalizer.Canonicalize(
            new CanonicalLineageDocument(
                LineageSchema,
                Convert.ToHexStringLower(SHA256.HashData(canonicalBytes)),
                derivation.DerivationSha256,
                [.. tripwires.SelectMany(static tripwire => tripwire.Lines).Select(CanonicalLineLineageDocument.Of)]),
            LineageSchema + "-canonical-json",
            64);

        return new EuCorrigendumTripwireSet(
            derivation.DerivationSha256,
            tripwires.AsReadOnly(),
            orderedGaps.AsReadOnly(),
            canonicalBytes,
            lineageBytes);
    }

    private static IReadOnlyList<string> BuildServedBodyLanguageIris()
    {
        var spelled = new Dictionary<EuOfficialLanguage, string>
        {
            [EuOfficialLanguage.English] = LanguageAuthorityBase + "ENG",
            [EuOfficialLanguage.French] = LanguageAuthorityBase + "FRA",
        };
        var iris = new List<string>();
        foreach (var language in EuLanguageBodyDisposition.BodyCandidateLanguages)
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

    private sealed record CanonicalGapDocument(
        string ObjectPublisherUri,
        string CanonicalWorkRoot,
        string Reason,
        string CorrectsAcquisition)
    {
        public static CanonicalGapDocument Of(EuCorrigendumTripwireUnresolvedGap gap) =>
            new(gap.ObjectPublisherUri, gap.CanonicalWorkRoot, gap.Reason.ToString(), gap.CorrectsAcquisition.ToString());
    }

    private sealed record CanonicalSetDocument(
        string Schema,
        IReadOnlyList<EuCorrigendumTripwire.CanonicalTripwireDocument> Tripwires,
        IReadOnlyList<CanonicalGapDocument> UnresolvedGaps);

    private sealed record CanonicalLineLineageDocument(
        string CorrectedWorkRoot,
        string CorrigendumWorkRoot,
        string PublisherExpressionId,
        IReadOnlyList<string> LineageContentSha256InOrder,
        IReadOnlyList<CanonicalEvidenceDocument> CorrectsEvidenceRefs)
    {
        public static CanonicalLineLineageDocument Of(EuCorrigendumTripwireLine line) =>
            new(
                line.CorrectedWorkRoot,
                line.CorrigendumWorkRoot,
                line.PublisherExpressionId,
                line.LineageContentSha256InOrder,
                [.. line.CorrectsEvidenceRefs.Select(static reference => new CanonicalEvidenceDocument(reference.ResourceId, reference.Sha256))]);
    }

    private sealed record CanonicalEvidenceDocument(string ResourceId, string Sha256);

    private sealed record CanonicalLineageDocument(
        string Schema,
        string CanonicalSha256,
        string DerivationSha256,
        IReadOnlyList<CanonicalLineLineageDocument> Lines);
}
