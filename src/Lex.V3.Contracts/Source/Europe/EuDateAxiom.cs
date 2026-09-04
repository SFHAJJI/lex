using System.Text.Json.Serialization;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One EU date exactly as R4 requires it be kept, built on the already-merged Facts date layer
/// rather than beside it. Stage 2 item E1, ledger row <c>SRC-013</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a rework, not the original E1 head. The first version (e5f47f4f) built a whole parallel
/// vocabulary next to <see cref="Lex.V3.Contracts.Facts"/>: its own role enum with no wire tokens,
/// its own precision enum, a copied datatype table, a hand rolled timezone rule, a third sentinel
/// literal, and a <c>ParsedAuthorityLabel</c> field that actually held the fd_335 label rather than
/// naming who produced the reading. The design objection (coordination/EVENTS.md event
/// <c>lex-event-20260904T020125653Z-abdc750beb044cac843320c1d256d6e7</c>) and its precision
/// (<c>lex-event-20260904T021531772Z-c01ed3ed0faa4303b6f2eb72279fc53e</c>) are both authoritative
/// over the summary below.
/// </para>
/// <para>
/// The ruled shape: one role vocabulary (<see cref="DateSemanticRole"/>, already merged); a pinned
/// fd_335 qualifier table retargeted to it; the two predicates review/23-research-temporal.md
/// section 3 shows with no owl:Axiom qualifier example
/// (<c>resource_legal_date_end-of-validity</c> and <c>resource_legal_date_signature</c>) deriving
/// their role from the predicate identity alone, on the same basis; anything else typed
/// <see cref="DateSemanticRole.RoleNotStatedByPublisher"/>; precision, the datatype table, the
/// timezone rule and the open sentinel reused from <see cref="PublisherDate"/> rather than
/// reimplemented; and a genuinely new EU-specific binding
/// (<see cref="EuDateAxiomBinding"/>) that holds the <c>owl:Axiom</c> reference, the raw fd_335
/// code, the fd_335 label as its own field (never conflated with who produced the reading), the
/// publisher comment, the NAL scheme identity, and the <see cref="PublisherDateFact"/> the EU
/// evidence produced.
/// </para>
/// <para>
/// Single role home (the ruling's first precision): <see cref="EuDateAxiomBinding"/> carries no
/// field of its own named <c>Role</c> or <c>SemanticRole</c>. The role is computed exactly once,
/// inside <see cref="EuDateAxiomBinding.Create"/>, and lives only on the
/// <see cref="PublisherDateFact"/> the binding holds (<see cref="EuDateAxiomBinding.Fact"/>). A
/// reader who wants the role reads <c>binding.Fact.SemanticRole</c>; nothing here mirrors it onto a
/// second property that could drift from that one.
/// </para>
/// </remarks>
public sealed class EuDateAxiomBinding
{
    private EuDateAxiomBinding(
        PublisherDateFact fact,
        string? qualifierLabel,
        EuNalSchemeIdentity schemeIdentity)
    {
        Fact = fact;
        QualifierLabel = qualifierLabel;
        SchemeIdentity = schemeIdentity;
    }

    /// <summary>
    /// The publisher date fact this binding qualifies. The single, only home for the semantic
    /// role: read <c>Fact.SemanticRole</c>, never a role mirrored here.
    /// </summary>
    public PublisherDateFact Fact { get; }

    /// <summary>
    /// The fd_335 label exactly as observed (e.g. "Entry into force"), or null when none applies.
    /// Its own field, distinct from <see cref="ParsedByAuthority"/>: the label is what the
    /// publisher's NAL calls the qualifier code, not who produced the semantic-role reading.
    /// </summary>
    public string? QualifierLabel { get; }

    /// <summary>The pinned NAL scheme this binding's qualifier vocabulary is drawn from.</summary>
    public EuNalSchemeIdentity SchemeIdentity { get; }

    /// <summary>
    /// The identity of the EU work (act) this date belongs to, needed independently of
    /// <see cref="Fact"/> so the transposition classification can read it without unpacking the
    /// fact. Backed entirely by <see cref="PublisherDateFact.Subject"/>: never a second stored
    /// copy that the fact's own subject could drift away from.
    /// </summary>
    public OfficialIdentitySet WorkIdentity => Fact.Subject;

    /// <summary>The reified <c>owl:Axiom</c> reference, read through the fact it qualifies.</summary>
    public QualifiedAxiom Axiom => Fact.Axiom;

    /// <summary>The fd_335 token exactly as observed (e.g. "EV", "AU+TARD"), or null if none.</summary>
    public string? RawQualifierCode => Fact.RawQualifier;

    /// <summary>The publisher's <c>comment_on_date</c> text, or null if none.</summary>
    public string? PublisherComment => Fact.PublisherComment;

    /// <summary>Who produced the <see cref="PublisherDateFact.SemanticRole"/> reading, as an https URI.</summary>
    public string ParsedByAuthority => Fact.ParsedByAuthority;

    /// <summary>
    /// The only path that mints a binding. Builds the <see cref="PublisherDate"/> and the
    /// <see cref="PublisherDateFact"/> it wraps in one step, so the role is computed exactly once
    /// and the sentinel, precision and calendar validity are exactly whatever
    /// <see cref="PublisherDate"/>'s own constructor already enforces.
    /// </summary>
    /// <param name="work">The EU work (act) this date is about.</param>
    /// <param name="rawLexicalValue">The publisher's date value exactly as served, e.g. "2016-05-24".</param>
    /// <param name="datatypeUri">One of the three <see cref="PublisherDate.PrecisionByDatatype"/> XSD date datatypes.</param>
    /// <param name="precision">The precision <paramref name="datatypeUri"/> expresses. Must agree with it.</param>
    /// <param name="sourcePredicateUri">The exact CDM predicate this date was observed on.</param>
    /// <param name="axiom">The reified <c>owl:Axiom</c> statement, with its qualifiers.</param>
    /// <param name="rawQualifierCode">The fd_335 token exactly as observed, or null if none was present.</param>
    /// <param name="qualifierLabel">The fd_335 label exactly as observed, or null if none applies.</param>
    /// <param name="publisherComment">The publisher's <c>comment_on_date</c> text, or null.</param>
    /// <param name="parsedByAuthority">Who produced the semantic-role reading, as an absolute https URI.</param>
    /// <param name="sourceObservationId">The custody coordinate for the observation this date came from.</param>
    public static EuDateAxiomBinding Create(
        OfficialIdentitySet work,
        string rawLexicalValue,
        string datatypeUri,
        DatePrecision precision,
        string sourcePredicateUri,
        QualifiedAxiom axiom,
        string? rawQualifierCode,
        string? qualifierLabel,
        string? publisherComment,
        string parsedByAuthority,
        string sourceObservationId)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(axiom);

        var role = ComputeRole(sourcePredicateUri, rawQualifierCode, qualifierLabel);
        var openSentinel = ComputeOpenSentinel(rawLexicalValue, datatypeUri);
        var date = new PublisherDate(PublisherDate.Identity, rawLexicalValue, datatypeUri, precision, openSentinel);

        var fact = new PublisherDateFact(
            PublisherDateFact.Identity,
            work,
            date,
            sourcePredicateUri,
            axiom,
            rawQualifierCode,
            publisherComment,
            role,
            TranspositionEvidence.None,
            parsedByAuthority,
            sourceObservationId);

        return new EuDateAxiomBinding(fact, qualifierLabel, EuNalSchemeIdentity.Fd335);
    }

    /// <summary>
    /// Whether the raw lexical value, at this datatype, is <see cref="PublisherDate"/>'s own
    /// open-end sentinel. Computed from <see cref="PublisherDate"/>'s own public statics
    /// (<see cref="PublisherDate.DatePart"/>, <see cref="PublisherDate.OpenEndedLexicalValue"/>,
    /// <see cref="PublisherDate.Date"/>) rather than a second copy of that rule: the actual
    /// admission or refusal of a malformed near-sentinel value still happens exactly once, inside
    /// <see cref="PublisherDate"/>'s own constructor, immediately after this call.
    /// </summary>
    private static DateOpenSentinel ComputeOpenSentinel(string rawLexicalValue, string datatypeUri)
    {
        var isSentinelValue =
            string.Equals(
                PublisherDate.DatePart(rawLexicalValue), PublisherDate.OpenEndedLexicalValue,
                StringComparison.Ordinal) &&
            string.Equals(datatypeUri, PublisherDate.Date, StringComparison.Ordinal);
        return isSentinelValue ? DateOpenSentinel.OpenEnded : DateOpenSentinel.NotOpen;
    }

    /// <summary>
    /// The only place a EU date's <see cref="DateSemanticRole"/> is decided. Never a caller
    /// supplied parameter: only <paramref name="rawQualifierCode"/>, <paramref name="qualifierLabel"/>
    /// and, for the two predicate-evidenced roles, <paramref name="sourcePredicateUri"/> ever
    /// select it, so there is no channel through which a date's order relative to any other date
    /// could reach the role even by accident.
    /// </summary>
    private static DateSemanticRole ComputeRole(
        string sourcePredicateUri, string? rawQualifierCode, string? qualifierLabel)
    {
        if (rawQualifierCode is not null &&
            EuDateQualifierVocabulary.PinnedQualifiers.TryGetValue(rawQualifierCode, out var pin))
        {
            if (!string.Equals(sourcePredicateUri, pin.PredicateUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"fd_335 \"{rawQualifierCode}\" is only evidenced on {pin.PredicateUri}, not on " +
                    $"{sourcePredicateUri}.",
                    nameof(sourcePredicateUri));
            }

            if (!string.Equals(qualifierLabel, pin.Label, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"fd_335 \"{rawQualifierCode}\" is pinned to the label \"{pin.Label}\" in " +
                    $"{EuNalSchemeIdentity.Fd335Name}; " +
                    (qualifierLabel is null ? "no label was given." : $"\"{qualifierLabel}\" does not match."),
                    nameof(qualifierLabel));
            }

            return pin.Role;
        }

        if (rawQualifierCode is null)
        {
            if (string.Equals(
                    sourcePredicateUri, EuDateQualifierVocabulary.EndOfValidityPredicateUri,
                    StringComparison.Ordinal))
            {
                if (qualifierLabel is not null)
                {
                    throw new ArgumentException(
                        "review/23 records no fd_335 qualifier example for end-of-validity; a label " +
                        "cannot be asserted for it.",
                        nameof(qualifierLabel));
                }

                return DateSemanticRole.EndOfValidity;
            }

            if (string.Equals(
                    sourcePredicateUri, EuDateQualifierVocabulary.SignatureDatePredicateUri,
                    StringComparison.Ordinal))
            {
                if (qualifierLabel is not null)
                {
                    throw new ArgumentException(
                        "review/23 records no fd_335 qualifier example for signature; a label cannot " +
                        "be asserted for it.",
                        nameof(qualifierLabel));
                }

                return DateSemanticRole.SignatureDate;
            }
        }

        // Absent, or present but outside the pinned set: typed unknown either way, and never
        // reclassified by which predicate it happens to sit on beyond the two checks above.
        return DateSemanticRole.RoleNotStatedByPublisher;
    }
}

/// <summary>
/// The pinned NAL scheme identity for EU date qualifiers, carrying more than the bare short name.
/// </summary>
/// <remarks>
/// review/23-research-temporal.md section 8 documents the NAL authority-table resource shape
/// through the sibling <c>dir-eu-legal-act</c> NAL:
/// <c>http://publications.europa.eu/resource/authority/dir-eu-legal-act/06202020</c>, and lists
/// "date qualifiers fd_335" among the NALs that "resolve in the endpoint" the same way. A bare
/// string "fd_335" drives nothing a reader could resolve back to the publisher's own distribution;
/// this type carries the resource-base URI the same shape implies instead.
/// </remarks>
public sealed record EuNalSchemeIdentity
{
    public const string Fd335Name = "fd_335";

    /// <summary>
    /// The NAL's own Cellar authority-table resource base URI, in the exact shape review/23
    /// section 8 shows for the sibling <c>dir-eu-legal-act</c> NAL.
    /// </summary>
    public const string Fd335AuthorityResourceBaseUri =
        "http://publications.europa.eu/resource/authority/fd_335";

    private EuNalSchemeIdentity(string name, string authorityResourceBaseUri)
    {
        Name = name;
        AuthorityResourceBaseUri = authorityResourceBaseUri;
    }

    public string Name { get; }

    public string AuthorityResourceBaseUri { get; }

    /// <summary>The one NAL scheme this lane evidences. No other NAL is pinned here.</summary>
    public static EuNalSchemeIdentity Fd335 { get; } = new(Fd335Name, Fd335AuthorityResourceBaseUri);
}

/// <summary>
/// The pinned NAL scheme and the exact predicate/label pairing review/23 records for each
/// evidenced fd_335 token, retargeted onto <see cref="DateSemanticRole"/> so the EU binding and the
/// merged Facts layer classify against one closed role vocabulary rather than two.
/// </summary>
public static class EuDateQualifierVocabulary
{
    private const string Cdm = "http://publications.europa.eu/ontology/cdm#";

    /// <summary>
    /// The one multi-valued predicate review/23 shows carrying both the EV and MA qualifiers.
    /// </summary>
    public const string EntryIntoForceAndApplicationPredicateUri =
        Cdm + "resource_legal_date_entry-into-force";

    /// <summary>The predicate review/23 shows carrying the AU+TARD qualifier.</summary>
    public const string DeadlinePredicateUri = Cdm + "resource_legal_date_deadline";

    /// <summary>
    /// The predicate review/23 shows carrying the <c>9999-12-31</c> open sentinel, with no
    /// observed qualifier example. Evidences <see cref="DateSemanticRole.EndOfValidity"/> by the
    /// predicate identity alone.
    /// </summary>
    public const string EndOfValidityPredicateUri = Cdm + "resource_legal_date_end-of-validity";

    /// <summary>
    /// The bare CDM property review/23 section 3's property list names
    /// (<c>resource_legal_date_signature</c>), with no owl:Axiom qualifier example either.
    /// Evidences <see cref="DateSemanticRole.SignatureDate"/> on the same basis as
    /// <see cref="EndOfValidityPredicateUri"/>: the observed predicate inventory is itself the
    /// evidence for both, per the ruling's correction of the original E1 head's narrower reading.
    /// </summary>
    public const string SignatureDatePredicateUri = Cdm + "resource_legal_date_signature";

    /// <summary>One pinned fd_335 token's expected predicate, label and role, together.</summary>
    internal sealed record Pin(string PredicateUri, string Label, DateSemanticRole Role);

    /// <summary>
    /// The exactly three fd_335 tokens review/23 evidences, each retargeted to its
    /// <see cref="DateSemanticRole"/> member. Adding a fourth here without an observed
    /// <c>owl:Axiom</c> example in review/23 repeats the mistake the original E1 head's scope
    /// ruling already corrected for the signature role.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Pin> PinnedQualifiers =
        new Dictionary<string, Pin>(StringComparer.Ordinal)
        {
            ["EV"] = new Pin(
                EntryIntoForceAndApplicationPredicateUri, "Entry into force", DateSemanticRole.EntryIntoForce),
            ["MA"] = new Pin(
                EntryIntoForceAndApplicationPredicateUri, "Application", DateSemanticRole.ApplicationDate),
            ["AU+TARD"] = new Pin(
                DeadlinePredicateUri, "At the latest", DateSemanticRole.PublisherDeadline),
        };
}

/// <summary>
/// The two CDM legal subclasses this lane's transposition classification distinguishes, asserted
/// as a publisher fact rather than trusted on a caller's bare claim.
/// </summary>
/// <remarks>
/// review/23-research-temporal.md section 3 lists <c>regulation</c> and <c>directive</c> among the
/// CDM legal subclasses of <c>cdm:resource_legal</c>; section 6 gives the reason only these two
/// matter here: "GDPR, being a regulation, has no NIM links ... so 'transposition' questions only
/// make sense for directives." A third subclass (decision, and so on) is not needed by anything
/// this lane classifies and is not added ahead of that need.
/// </remarks>
public enum EuWorkKind
{
    [JsonStringEnumMemberName("directive")]
    Directive = 1,

    [JsonStringEnumMemberName("regulation")]
    Regulation = 2,
}

/// <summary>
/// A publisher assertion that one EU work is of a given CDM legal subclass. Modelled on the RDF
/// shape <c>?work rdf:type cdm:{subclass}</c>, kept as its own fact so the transposition
/// classification can check for it rather than trust the caller's claim that a work is a directive.
/// </summary>
public sealed record EuWorkKindAssertion
{
    public EuWorkKindAssertion(OfficialIdentitySet work, EuWorkKind kind)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        Kind = Lex.V3.Contracts.ContractValidation.RequireDefined(kind, nameof(kind));
    }

    public OfficialIdentitySet Work { get; }

    public EuWorkKind Kind { get; }

    /// <summary>Whether this assertion names <paramref name="work"/> as a directive.</summary>
    public bool AssertsDirectiveFor(OfficialIdentitySet work) =>
        Kind == EuWorkKind.Directive && work is not null && Work.SameIdentity(work);
}

/// <summary>
/// The outcome of asking whether one <see cref="EuDateAxiomBinding"/> is a transposition deadline.
/// </summary>
/// <remarks>
/// <see cref="DateSemanticRole.TranspositionDeadline"/> is never assigned by
/// <see cref="EuDateAxiomBinding.Create"/> itself: a fresh binding's fd_335 <c>AU+TARD</c> token
/// yields <see cref="DateSemanticRole.PublisherDeadline"/> with
/// <see cref="TranspositionEvidence.None"/>, exactly matching the merged
/// <see cref="PublisherDateFact"/> constructor's own invariant that a transposition deadline
/// requires directive-qualifier or NIM evidence. This is the derived classification that produces
/// that stronger reading when, and only when, it is actually justified.
/// </remarks>
public enum EuTranspositionDeadlineOutcome
{
    /// <summary>The binding's own role is not <see cref="DateSemanticRole.PublisherDeadline"/>; the question does not apply.</summary>
    NotADeadline = 1,

    /// <summary>
    /// A PublisherDeadline-role binding was presented with no directive evidence, or with evidence
    /// that does not name the binding's own work, or naming a work not asserted as a directive.
    /// The underlying binding is unchanged; this is the typed record that promotion was attempted
    /// and refused.
    /// </summary>
    TranspositionDeadlineEvidenceInsufficient = 2,

    /// <summary>
    /// A PublisherDeadline-role binding whose directive evidence names its own work, and whose
    /// work is asserted as a directive. <see cref="EuTranspositionDeadlineClassification.PromotedFact"/>
    /// carries the resulting <see cref="DateSemanticRole.TranspositionDeadline"/> fact.
    /// </summary>
    AcceptedTranspositionDeadline = 3,
}

/// <summary>
/// Directive-specific publisher evidence tying one PublisherDeadline-role binding to a Member
/// State's transposition obligation for that directive.
/// </summary>
/// <remarks>
/// The directive identity is checked by CELEX or ELI shape, reusing
/// <see cref="OfficialIdentifier"/>'s own grammar rather than a hand rolled ASCII check: shape
/// alone is not sufficient, so <see cref="EuTranspositionDeadlineClassification.Classify"/> also
/// requires it to name the exact same work as the binding it accompanies.
/// </remarks>
public sealed class EuDirectiveTranspositionEvidence
{
    public EuDirectiveTranspositionEvidence(OfficialIdentifier directiveIdentity)
    {
        ArgumentNullException.ThrowIfNull(directiveIdentity);
        if (directiveIdentity.Family is not (FactsIdentifierFamily.Celex or FactsIdentifierFamily.Eli))
        {
            throw new ArgumentException(
                "Directive transposition evidence must name the directive by CELEX or ELI shape, " +
                $"not {directiveIdentity.Family}.",
                nameof(directiveIdentity));
        }

        DirectiveIdentity = directiveIdentity;
    }

    /// <summary>
    /// The directive this deadline transposes, by its own CELEX or ELI identity. Shape alone does
    /// not accept it: <see cref="EuTranspositionDeadlineClassification.Classify"/> also requires it
    /// to match the classified binding's own <see cref="EuDateAxiomBinding.WorkIdentity"/>.
    /// </summary>
    public OfficialIdentifier DirectiveIdentity { get; }
}

/// <summary>
/// A derived classification of one PublisherDeadline-role <see cref="EuDateAxiomBinding"/> as a
/// transposition deadline or not.
/// </summary>
/// <remarks>
/// Mirrors the <c>derived_from</c> pattern this codebase already uses elsewhere
/// (<see cref="Lex.V3.Contracts.Facts.DerivedInverseRelation.DerivedFrom"/>,
/// <c>Lex.V3.Contracts.Source.Luxembourg.LuxembourgLocalInboundView.DerivedFrom</c>): the result
/// names exactly what it was derived from rather than standing on its own as a fresh fact, and the
/// promoted role lives only on <see cref="PromotedFact"/>, never mirrored back onto
/// <see cref="DerivedFrom"/>.
/// </remarks>
public sealed class EuTranspositionDeadlineClassification
{
    private EuTranspositionDeadlineClassification(
        EuTranspositionDeadlineOutcome outcome,
        EuDateAxiomBinding derivedFrom,
        EuDirectiveTranspositionEvidence? evidence,
        PublisherDateFact? promotedFact)
    {
        Outcome = outcome;
        DerivedFrom = derivedFrom;
        Evidence = evidence;
        PromotedFact = promotedFact;
    }

    public EuTranspositionDeadlineOutcome Outcome { get; }

    /// <summary>The exact binding this classification was derived from.</summary>
    public EuDateAxiomBinding DerivedFrom { get; }

    /// <summary>The directive evidence presented, if any, regardless of outcome.</summary>
    public EuDirectiveTranspositionEvidence? Evidence { get; }

    /// <summary>
    /// The single new <see cref="PublisherDateFact"/> carrying
    /// <see cref="DateSemanticRole.TranspositionDeadline"/> and
    /// <see cref="TranspositionEvidence.DirectiveQualifier"/>, present only for
    /// <see cref="EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline"/>. This is the only
    /// place the promoted role is ever recorded: <see cref="DerivedFrom"/>'s own
    /// <see cref="EuDateAxiomBinding.Fact"/> is untouched and still reports
    /// <see cref="DateSemanticRole.PublisherDeadline"/>.
    /// </summary>
    public PublisherDateFact? PromotedFact { get; }

    /// <summary>True only for <see cref="EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline"/>.</summary>
    public bool IsAcceptedTranspositionDeadline =>
        Outcome == EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline;

    /// <summary>The only path that classifies a binding.</summary>
    /// <param name="binding">The binding being asked whether it is a transposition deadline.</param>
    /// <param name="evidence">Directive-specific evidence, or null if none is offered.</param>
    /// <param name="workKindAssertions">
    /// Every publisher assertion available about a work's CDM legal subclass. Checked, not
    /// trusted: promotion requires one of these to assert <paramref name="binding"/>'s own work as
    /// a directive, not merely that the caller believes it is one.
    /// </param>
    public static EuTranspositionDeadlineClassification Classify(
        EuDateAxiomBinding binding,
        EuDirectiveTranspositionEvidence? evidence,
        IReadOnlyList<EuWorkKindAssertion> workKindAssertions)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(workKindAssertions);
        for (var index = 0; index < workKindAssertions.Count; index++)
        {
            if (workKindAssertions[index] is null)
            {
                throw new ArgumentException(
                    $"The work-kind assertion at {index} is null.", nameof(workKindAssertions));
            }
        }

        if (binding.Fact.SemanticRole != DateSemanticRole.PublisherDeadline)
        {
            if (evidence is not null)
            {
                throw new ArgumentException(
                    "Directive evidence can only accompany a PublisherDeadline-role binding.",
                    nameof(evidence));
            }

            return new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.NotADeadline, binding, null, null);
        }

        if (evidence is null)
        {
            return new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, binding, null, null);
        }

        // Shape alone is not enough: the directive evidence must name the exact same work this
        // binding's date belongs to, not merely any CELEX- or ELI-shaped identifier.
        var namesSameWork =
            binding.WorkIdentity.Has(evidence.DirectiveIdentity.Family) &&
            string.Equals(
                binding.WorkIdentity.Value(evidence.DirectiveIdentity.Family),
                evidence.DirectiveIdentity.RawValue,
                StringComparison.Ordinal);

        var assertedAsDirective = workKindAssertions.Any(
            assertion => assertion.AssertsDirectiveFor(binding.WorkIdentity));

        if (!namesSameWork || !assertedAsDirective)
        {
            return new EuTranspositionDeadlineClassification(
                EuTranspositionDeadlineOutcome.TranspositionDeadlineEvidenceInsufficient, binding, evidence, null);
        }

        var promoted = new PublisherDateFact(
            PublisherDateFact.Identity,
            binding.Fact.Subject,
            binding.Fact.Date,
            binding.Fact.SourcePredicateUri,
            binding.Fact.Axiom,
            binding.Fact.RawQualifier,
            binding.Fact.PublisherComment,
            DateSemanticRole.TranspositionDeadline,
            TranspositionEvidence.DirectiveQualifier,
            binding.Fact.ParsedByAuthority,
            binding.Fact.SourceObservationId);

        return new EuTranspositionDeadlineClassification(
            EuTranspositionDeadlineOutcome.AcceptedTranspositionDeadline, binding, evidence, promoted);
    }
}
