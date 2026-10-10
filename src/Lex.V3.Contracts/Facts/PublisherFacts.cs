using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The publisher-fact object graph.
///
/// Every type here exists to keep an official assertion distinguishable from anything
/// Lex computed about it. The rule the whole file serves: a reader must always be able
/// to tell what the publisher said from what we derived, without consulting a flag that
/// a future refactor could default.
///
/// That is why <see cref="PublisherRelation"/>, <see cref="DerivedInverseRelation"/> and
/// <see cref="LocalInboundView"/> are three distinct types rather than one carrying a
/// discriminator. A flag can be defaulted, copied, or lost in a projection. A type
/// cannot: code that accepts a publisher assertion will not compile against a derived
/// one, so the distinction survives refactoring rather than depending on discipline.
///
/// No type here names a storage provider, and none carries a locator. Custody of the
/// durable bytes belongs to the observation record in <c>http_observation/1</c>, reached
/// through <see cref="FactOccurrence.SourceObservationId"/>. Keeping one custody
/// coordinate per observation is the point: two would eventually disagree, and the
/// disagreement would be invisible.
/// </summary>
public static class FactsContractLine
{
    /// <summary>The frozen generation label for this contract family.</summary>
    public static string Generation => "lex-v3/facts/1";
}

/// <summary>
/// Implemented by every fact value that can be validated after deserialisation.
///
/// Presence of a member is enforced by <see cref="JsonRequiredAttribute"/>, but presence
/// is not enough: JSON null satisfies a required member. <see cref="Validate"/> closes
/// that gap, and <see cref="FactsJson"/> calls it on every read, so a fact that reaches
/// application code has already refused null provenance rather than carrying it.
/// </summary>
public interface IFactsValue
{
    /// <summary>Throws <see cref="FactsContractViolationException"/> when an invariant fails.</summary>
    void Validate();
}

/// <summary>
/// Raised when a fact value violates an invariant the type system cannot express.
/// Separate from <see cref="System.Text.Json.JsonException"/> so a contract violation is
/// never mistaken for malformed transport.
/// </summary>
public sealed class FactsContractViolationException : Exception
{
    /// <summary>Creates the exception with the invariant that failed.</summary>
    public FactsContractViolationException(string message)
        : base(message)
    {
    }
}

/// <summary>An official identifier as the publisher expressed it, never normalised away.</summary>
/// <param name="Scheme">The identifier system, for example an ELI or CELEX namespace.</param>
/// <param name="Value">The identifier exactly as published, preserving case and punctuation.</param>
public readonly record struct OfficialIdentity(
    [property: JsonRequired] string Scheme,
    [property: JsonRequired] string Value) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        FactsGuard.NotBlank(Scheme, "official_identity.scheme");
        FactsGuard.NotBlank(Value, "official_identity.value");
    }
}

/// <summary>
/// Where a fact came from, and which occurrence of it this is.
///
/// The observation id is the only custody coordinate a fact carries: no digest, no byte
/// count, no locator, no container, no URL. Those belong to the observation record, which
/// owns them once. A facts-side copy would be a second coordinate for one set of bytes,
/// and the two would drift.
///
/// <paramref name="Ordinal"/> is what keeps two byte-identical publisher rows from the
/// same observation distinct. Without it, a publisher that states the same thing twice
/// would be indistinguishable from one that stated it once, and any set, dictionary or
/// digest would quietly collapse the pair.
/// </summary>
/// <param name="SourceObservationId">Stable identity of the observation that carried this fact.</param>
/// <param name="Ordinal">The zero-based position of this fact within that observation.</param>
public readonly record struct FactOccurrence(
    [property: JsonRequired] string SourceObservationId,
    [property: JsonRequired] int Ordinal) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        FactsGuard.NotBlank(SourceObservationId, "occurrence.source_observation_id");
        if (Ordinal < 0)
        {
            throw new FactsContractViolationException(
                "occurrence.ordinal must be zero or greater; a negative occurrence has no source position.");
        }
    }
}

/// <summary>
/// One qualifier attached to a publisher assertion, retained verbatim.
///
/// Several may share a predicate URI, which is why the carrier is an ordered multimap
/// rather than a map: collapsing duplicates would silently discard a publisher statement.
/// <paramref name="OccurrenceOrdinal"/> makes even two byte-identical qualifiers distinct,
/// so no layer can deduplicate them on content.
/// </summary>
/// <param name="PredicateUri">The exact qualifying predicate URI.</param>
/// <param name="RawLexicalValue">The value exactly as published, before any parsing.</param>
/// <param name="Datatype">The declared datatype URI, when the publisher supplied one.</param>
/// <param name="RemoteAxiomUri">
/// The publisher's own axiom URI when present. This is provenance only. It is never the
/// local identity of the qualifier, because two publishers may reuse a URI and because a
/// missing one must not prevent the qualifier existing.
/// </param>
/// <param name="OccurrenceOrdinal">Zero-based position within the assertion, as published.</param>
public readonly record struct QualifiedAxiom(
    [property: JsonRequired] string PredicateUri,
    [property: JsonRequired] string RawLexicalValue,
    [property: JsonRequired] string? Datatype,
    [property: JsonRequired] string? RemoteAxiomUri,
    [property: JsonRequired] int OccurrenceOrdinal) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        FactsGuard.NotBlank(PredicateUri, "qualified_axiom.predicate_uri");
        FactsGuard.NotNull(RawLexicalValue, "qualified_axiom.raw_lexical_value");
        if (OccurrenceOrdinal < 0)
        {
            throw new FactsContractViolationException(
                "qualified_axiom.occurrence_ordinal must be zero or greater.");
        }
    }
}

/// <summary>Whether a publisher date is bounded, or explicitly open.</summary>
public enum OpenSentinelState
{
    /// <summary>The publisher gave a bounded value.</summary>
    Bounded,

    /// <summary>The publisher used its own open-ended sentinel, retained as such.</summary>
    OpenEnded,
}

/// <summary>
/// The precision the publisher declared or evidently used. Closed, because an
/// unrecognised precision is drift rather than a new member: silently accepting one
/// would let a year-precision date be read as a day.
/// </summary>
public enum DatePrecision
{
    /// <summary>Year only.</summary>
    Year,

    /// <summary>Year and month.</summary>
    Month,

    /// <summary>Calendar day.</summary>
    Day,

    /// <summary>A point in time within a day.</summary>
    Instant,
}

/// <summary>
/// A date exactly as the publisher expressed it, with every component that could carry
/// meaning. Nothing here is inferred: in particular <see cref="SemanticRole"/> comes from
/// the source predicate or an explicit qualifier, never from the order dates appear in.
/// </summary>
/// <param name="RawLexicalValue">The value as published, before parsing.</param>
/// <param name="Datatype">Declared RDF datatype URI, when supplied.</param>
/// <param name="Precision">Declared or evident precision. Null when the publisher did not say.</param>
/// <param name="SourcePredicateUri">The predicate that carried this date.</param>
/// <param name="RemoteAxiomUri">Publisher axiom URI, provenance only.</param>
/// <param name="RawQualifier">The qualifier microsyntax as published, unparsed.</param>
/// <param name="ParsedAuthorityIdentity">
/// The authority resolved from the qualifier, when it resolved. Null means unresolved,
/// which is a fact about our knowledge and never a reason to discard the date.
/// </param>
/// <param name="PublisherComment">The publisher's own comment, retained verbatim.</param>
/// <param name="SemanticRole">
/// The role the publisher assigned, for example entry into force or a deadline. Null when
/// the publisher did not say. It is never inferred from position.
/// </param>
/// <param name="OpenSentinel">Whether the publisher marked this as open-ended.</param>
public sealed record PublisherDate(
    [property: JsonRequired] string RawLexicalValue,
    [property: JsonRequired] string? Datatype,
    [property: JsonRequired] DatePrecision? Precision,
    [property: JsonRequired] string SourcePredicateUri,
    [property: JsonRequired] string? RemoteAxiomUri,
    [property: JsonRequired] string? RawQualifier,
    [property: JsonRequired] string? ParsedAuthorityIdentity,
    [property: JsonRequired] string? PublisherComment,
    [property: JsonRequired] string? SemanticRole,
    [property: JsonRequired] OpenSentinelState OpenSentinel) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        FactsGuard.NotBlank(RawLexicalValue, "publisher_date.raw_lexical_value");
        FactsGuard.NotBlank(SourcePredicateUri, "publisher_date.source_predicate_uri");
    }
}

/// <summary>
/// A publisher date bound to the object it was stated about and to the observation that
/// carried it. The date value alone is not a fact: without a subject and an occurrence it
/// cannot be checked against the source, which is the whole product claim.
/// </summary>
/// <param name="Subject">The object the publisher stated this date about.</param>
/// <param name="Date">The date exactly as published.</param>
/// <param name="Occurrence">Observation and position this date was read from.</param>
public sealed record PublisherDateFact(
    [property: JsonRequired] OfficialIdentity Subject,
    [property: JsonRequired] PublisherDate Date,
    [property: JsonRequired] FactOccurrence Occurrence) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        Subject.Validate();
        FactsGuard.NotNull(Date, "publisher_date_fact.date");
        Date.Validate();
        Occurrence.Validate();
    }
}

/// <summary>
/// Why a relation target is not held as a body. Closed: an unrecognised value is drift,
/// not a new member, so vocabulary changes fail rather than deserialising silently.
/// </summary>
public enum UnheldTargetReason
{
    /// <summary>Outside the accepted body scope of the current source manifest.</summary>
    OutsideBodyScope,

    /// <summary>In scope, and not yet observed as held.</summary>
    NotYetHeld,

    /// <summary>A Cellar or CELEX case relation whose ECLI the publisher did not supply.</summary>
    EcliMissing,
}

/// <summary>
/// The other end of a relation. A target with official identity but no held body is a
/// first-class legal object, not an absence: discarding the edge would lose a publisher
/// assertion, and inventing an identifier would fabricate one.
/// </summary>
/// <param name="Identity">The publisher's identity for the target.</param>
/// <param name="IsHeld">Whether Lex holds a body for this target.</param>
/// <param name="UnheldReason">Why not, when <paramref name="IsHeld"/> is false.</param>
public sealed record RelationTarget(
    [property: JsonRequired] OfficialIdentity Identity,
    [property: JsonRequired] bool IsHeld,
    [property: JsonRequired] UnheldTargetReason? UnheldReason) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        Identity.Validate();
        if (IsHeld && UnheldReason is not null)
        {
            throw new FactsContractViolationException(
                "relation_target.unheld_reason must be null when the body is held; a held target has no reason to explain.");
        }

        if (!IsHeld && UnheldReason is null)
        {
            throw new FactsContractViolationException(
                "relation_target.unheld_reason is required when the body is not held; an unexplained absence is indistinguishable from a dropped edge.");
        }
    }
}

/// <summary>
/// The closed set of relation facts.
///
/// The hierarchy is closed by a <c>private protected</c> constructor, so no assembly can
/// add a fourth variant that consumers of the union would silently fail to handle. The
/// union serialises with an explicit tag and its variants cannot deserialise into one
/// another, because each requires its own provenance and unmapped members are refused.
/// </summary>
public abstract record RelationFact : IFactsValue
{
    private protected RelationFact()
    {
    }

    /// <summary>
    /// The wire tag identifying this variant. Never inferred from shape.
    ///
    /// The CLR name deliberately does not map to the wire name. An ignored property whose
    /// JSON name is <c>fact_kind</c> would be matched and skipped by the reader rather
    /// than reported as unmapped, so a tag appearing in an untagged position, at any
    /// nesting depth, would vanish silently instead of failing. With the names apart, a
    /// stray tag is an unmapped member and is refused wherever it appears.
    /// </summary>
    [JsonIgnore]
    public abstract string Variant { get; }

    /// <inheritdoc/>
    public abstract void Validate();
}

/// <summary>
/// A relation the publisher asserted, in the direction the publisher asserted it.
///
/// This type exists only for official assertions. Anything Lex computes uses
/// <see cref="DerivedInverseRelation"/> or <see cref="LocalInboundView"/>, so that no
/// projection can present a computed edge as a publisher statement.
/// </summary>
/// <param name="Source">The asserting end, as the publisher identified it.</param>
/// <param name="Target">The target end.</param>
/// <param name="PredicateUri">The exact predicate URI, never a normalised label.</param>
/// <param name="Axioms">
/// Every qualifier the publisher attached, as an ordered multimap: duplicate predicate
/// URIs are retained in the published order, because collapsing or reordering them would
/// report our arrangement as the publisher's.
/// </param>
/// <param name="Occurrence">The observation and position this assertion was read from.</param>
public sealed record PublisherRelation(
    [property: JsonRequired] OfficialIdentity Source,
    [property: JsonRequired] RelationTarget Target,
    [property: JsonRequired] string PredicateUri,
    [property: JsonRequired] IReadOnlyList<QualifiedAxiom> Axioms,
    [property: JsonRequired] FactOccurrence Occurrence) : RelationFact
{
    /// <summary>The wire tag for a publisher assertion.</summary>
    public const string Kind = "publisher_relation";

    /// <inheritdoc/>
    ///
    /// The attribute is repeated on every override deliberately. A JsonIgnore on the
    /// abstract declaration is not inherited by an override, so omitting it here would
    /// emit the union tag into the untagged shape as an ordinary member, and every
    /// closed schema would then reject the contract's own output.
    [JsonIgnore]
    public override string Variant => Kind;

    /// <inheritdoc/>
    public override void Validate()
    {
        Source.Validate();
        FactsGuard.NotNull(Target, "publisher_relation.target");
        Target.Validate();
        FactsGuard.NotBlank(PredicateUri, "publisher_relation.predicate_uri");
        FactsGuard.NotNull(Axioms, "publisher_relation.axioms");
        Occurrence.Validate();
        foreach (var axiom in Axioms)
        {
            axiom.Validate();
        }
    }

    /// <summary>
    /// Structural over the axiom sequence, including order and duplicates.
    ///
    /// The compiler-generated equality would compare <see cref="Axioms"/> by reference,
    /// so two relations carrying identical axioms would be unequal. Anything that
    /// deduplicates, digests or compares relations would then be silently wrong, and the
    /// failure would look like missing data rather than a broken comparison.
    ///
    /// Order is significant because the publisher's ordering is itself an observation we
    /// do not reorder, and duplicates are significant because a repeated predicate is a
    /// repeated statement.
    /// </summary>
    public bool Equals(PublisherRelation? other) =>
        other is not null
        && Source.Equals(other.Source)
        && Target == other.Target
        && string.Equals(PredicateUri, other.PredicateUri, StringComparison.Ordinal)
        && Occurrence.Equals(other.Occurrence)
        && Axioms.SequenceEqual(other.Axioms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(typeof(PublisherRelation));
        hash.Add(Source);
        hash.Add(Target);
        hash.Add(PredicateUri, StringComparer.Ordinal);
        hash.Add(Occurrence);
        foreach (var axiom in Axioms)
        {
            hash.Add(axiom);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// An inverse the ontology authorises, derived locally from exactly one publisher
/// assertion. Distinct from <see cref="PublisherRelation"/> by type, so it can never be
/// mistaken for something the publisher said.
///
/// The predicate URI is deliberately not required to differ from the forward assertion's.
/// A symmetric property authorises a transpose that carries the same URI, and refusing it
/// would drop a correct edge. What prevents the confusion is the closed union tag and the
/// distinct type, which no reader can forget to check.
/// </summary>
/// <param name="Source">The derived source, which is the assertion's target.</param>
/// <param name="Target">The derived target, which is the assertion's source.</param>
/// <param name="PredicateUri">The inverse predicate URI the ontology authorises.</param>
/// <param name="DerivedFrom">
/// The single publisher assertion this transpose came from. Exactly one, so the derivation
/// is always traceable back to one official statement.
/// </param>
/// <param name="AuthorisingOntologyUri">The ontology statement that authorises the inverse.</param>
public sealed record DerivedInverseRelation(
    [property: JsonRequired] OfficialIdentity Source,
    [property: JsonRequired] RelationTarget Target,
    [property: JsonRequired] string PredicateUri,
    [property: JsonRequired] PublisherRelation DerivedFrom,
    [property: JsonRequired] string AuthorisingOntologyUri) : RelationFact
{
    /// <summary>The wire tag for an ontology-authorised inverse.</summary>
    public const string Kind = "derived_inverse_relation";

    /// <inheritdoc/>
    ///
    /// The attribute is repeated on every override deliberately. A JsonIgnore on the
    /// abstract declaration is not inherited by an override, so omitting it here would
    /// emit the union tag into the untagged shape as an ordinary member, and every
    /// closed schema would then reject the contract's own output.
    [JsonIgnore]
    public override string Variant => Kind;

    /// <inheritdoc/>
    public override void Validate()
    {
        Source.Validate();
        FactsGuard.NotNull(Target, "derived_inverse_relation.target");
        Target.Validate();
        FactsGuard.NotBlank(PredicateUri, "derived_inverse_relation.predicate_uri");
        FactsGuard.NotNull(DerivedFrom, "derived_inverse_relation.derived_from");
        DerivedFrom.Validate();
        FactsGuard.NotBlank(
            AuthorisingOntologyUri,
            "derived_inverse_relation.authorising_ontology_uri");
    }
}

/// <summary>
/// A generic locally computed inbound view: what points at this object, assembled by Lex
/// rather than authorised by an ontology. Weaker than <see cref="DerivedInverseRelation"/>
/// and typed separately so the difference survives every projection.
/// </summary>
/// <param name="Subject">The object the view is about.</param>
/// <param name="Inbound">The publisher assertions that name it as target.</param>
public sealed record LocalInboundView(
    [property: JsonRequired] OfficialIdentity Subject,
    [property: JsonRequired] IReadOnlyList<PublisherRelation> Inbound) : RelationFact
{
    /// <summary>The wire tag for a locally derived inbound view.</summary>
    public const string Kind = "local_inbound_view";

    /// <inheritdoc/>
    ///
    /// The attribute is repeated on every override deliberately. A JsonIgnore on the
    /// abstract declaration is not inherited by an override, so omitting it here would
    /// emit the union tag into the untagged shape as an ordinary member, and every
    /// closed schema would then reject the contract's own output.
    [JsonIgnore]
    public override string Variant => Kind;

    /// <inheritdoc/>
    public override void Validate()
    {
        Subject.Validate();
        FactsGuard.NotNull(Inbound, "local_inbound_view.inbound");
        foreach (var relation in Inbound)
        {
            FactsGuard.NotNull(relation, "local_inbound_view.inbound[]");
            relation.Validate();
        }
    }

    /// <summary>Structural over the inbound sequence, for the same reason as
    /// <see cref="PublisherRelation.Equals(PublisherRelation?)"/>.</summary>
    public bool Equals(LocalInboundView? other) =>
        other is not null
        && Subject.Equals(other.Subject)
        && Inbound.SequenceEqual(other.Inbound);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(typeof(LocalInboundView));
        hash.Add(Subject);
        foreach (var relation in Inbound)
        {
            hash.Add(relation);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A vocabulary term the current manifest does not classify. This is a result, not an
/// exception path: it must reach run accounting so an unrecognised predicate is visible
/// rather than silently absent. Nothing downstream may default it to a known member.
/// </summary>
/// <param name="TermUri">The unrecognised term, exactly as published.</param>
/// <param name="EncounteredIn">The predicate or field position it appeared in.</param>
/// <param name="Occurrence">The observation and position that carried it.</param>
public sealed record VocabularyDrift(
    [property: JsonRequired] string TermUri,
    [property: JsonRequired] string EncounteredIn,
    [property: JsonRequired] FactOccurrence Occurrence) : IFactsValue
{
    /// <inheritdoc/>
    public void Validate()
    {
        FactsGuard.NotBlank(TermUri, "vocabulary_drift.term_uri");
        FactsGuard.NotBlank(EncounteredIn, "vocabulary_drift.encountered_in");
        Occurrence.Validate();
    }
}

/// <summary>Invariant guards shared by the fact graph.</summary>
internal static class FactsGuard
{
    internal static void NotNull(object? value, string member)
    {
        if (value is null)
        {
            throw new FactsContractViolationException(
                $"{member} is required and must not be null; a fact without it cannot be checked against its source.");
        }
    }

    internal static void NotBlank(string? value, string member)
    {
        NotNull(value, member);
        if (value!.Length == 0)
        {
            throw new FactsContractViolationException(
                $"{member} is required and must not be empty.");
        }
    }
}
