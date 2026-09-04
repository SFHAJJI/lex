using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// A located amendment axiom: one <c>cdm:resource_legal_amends_resource_legal</c> edge together
/// with every annotation the publisher reified onto it.
/// </summary>
/// <remarks>
/// <para>
/// Grounded in the retained fixture <c>amends-located-axioms.json</c> (2968 bytes, sha256
/// <c>d3353e41e9091b202970dae3ef5ec7be063a5b6dc5afcf30c12ada2b4fe01ffd</c>), five real located
/// axioms read from the EU SPARQL endpoint with every annotation the predicate carries. Four
/// things in that fixture decide this type's shape, and each of them contradicts a simpler design
/// that would otherwise look reasonable.
/// </para>
/// <para>
/// One: <see cref="Location"/> is a sequence of authority-qualified tokens, not a string. Each
/// token carries its own fd_370 member IRI inline. See <see cref="EuStructuralLocation"/>.
/// </para>
/// <para>
/// Two: <see cref="Role"/> is authority-qualified against fd_375, which is a <b>different</b> list
/// from the location's fd_370. Two lists are in play on one axiom, and an authority outside the
/// pinned list for its position is refused by name.
/// </para>
/// <para>
/// Three: <see cref="StartOfValidity"/> arrives in two spellings and is <b>optional</b>. Both
/// facts are read off the same five rows: two rows carry <c>2000-02-09</c>, two carry
/// <c>2010/02/01</c> and <c>2010/01/01</c>, and the third row carries no <c>start_of_validity</c>
/// binding at all. The optionality is worth stating separately because the E4 scope ruling records
/// only <c>end_of_validity</c> as optional, and the retained bytes show <c>start_of_validity</c>
/// is too.
/// </para>
/// <para>
/// Four: <see cref="EndOfValidity"/> is absent on all five, so it is optional on the strength of
/// observation rather than assumption.
/// </para>
/// <para>
/// <see cref="TypeOfLinkTarget"/> is <c>MS</c> on all five and is carried as observed rather than
/// closed to that one token. Five rows are not enough to close a publisher vocabulary, and the
/// cost of being wrong is refusing real data; the same reasoning keeps the fd_370 member codes
/// open in <see cref="EuAmendmentRelationVocabulary.LocationAuthorityListUri"/>.
/// </para>
/// </remarks>
public sealed class EuLocatedAmendmentAxiom
{
    private EuLocatedAmendmentAxiom(
        EuRelationEdgeBinding edge,
        EuStructuralLocation location,
        EuAuthorityQualifiedToken role,
        EuValidityDate? startOfValidity,
        EuValidityDate? endOfValidity,
        string typeOfLinkTarget,
        QualifiedAxiom axiom)
    {
        Edge = edge;
        Location = location;
        Role = role;
        StartOfValidity = startOfValidity;
        EndOfValidity = endOfValidity;
        TypeOfLinkTarget = typeOfLinkTarget;
        Axiom = axiom;
    }

    /// <summary>
    /// The amendment edge this axiom annotates, as a binding over the Facts layer's own
    /// <see cref="PublisherRelation"/> and <see cref="RelationFact"/>. Always on
    /// <see cref="EuAmendmentRelationVocabulary.AmendsPredicateUri"/> and always a publisher
    /// assertion, because that is the only direction the store holds.
    /// </summary>
    public EuRelationEdgeBinding Edge { get; }

    /// <summary>The structural location, as ordered authority-qualified tokens.</summary>
    public EuStructuralLocation Location { get; }

    /// <summary>The fd_375 amendment role. Observed members: <c>R</c>, <c>J</c>, <c>M</c>.</summary>
    public EuAuthorityQualifiedToken Role { get; }

    /// <summary>When the link starts to hold, or <c>null</c> where the publisher bound none.</summary>
    public EuValidityDate? StartOfValidity { get; }

    /// <summary>When the link stops holding, or <c>null</c>. Absent on all five retained rows.</summary>
    public EuValidityDate? EndOfValidity { get; }

    /// <summary>The link target type exactly as observed, for example <c>MS</c>.</summary>
    public string TypeOfLinkTarget { get; }

    /// <summary>
    /// The same five annotations as a Facts-layer <see cref="QualifiedAxiom"/>, each qualifier
    /// carrying the publisher's raw value under its own annotation predicate, in a fixed order:
    /// location, start, end, link-target type, role. Absent annotations contribute no qualifier.
    /// </summary>
    /// <remarks>
    /// Both views are kept on purpose. The typed properties above are what makes an amendment
    /// checkable; this one is what the Facts layer stores, and it holds the publisher's bytes
    /// rather than this type's reading of them, so a later reader can re-derive the typed view and
    /// disagree with it.
    /// </remarks>
    public QualifiedAxiom Axiom { get; }

    /// <summary>
    /// Reads one located amendment axiom from the publisher's raw annotation values.
    /// </summary>
    /// <param name="source">The amending act.</param>
    /// <param name="target">The amended act.</param>
    /// <param name="targetBodyScope">Whether <paramref name="target"/>'s own body is held.</param>
    /// <param name="rawLocation">The <c>reference_to_modified_location</c> value, verbatim.</param>
    /// <param name="rawRole">The <c>role2</c> value, verbatim, for example <c>{R|...fd_375/R}</c>.</param>
    /// <param name="rawStartOfValidity">The <c>start_of_validity</c> value, or null when unbound.</param>
    /// <param name="rawEndOfValidity">The <c>end_of_validity</c> value, or null when unbound.</param>
    /// <param name="typeOfLinkTarget">The <c>type_of_link_target</c> value, verbatim.</param>
    /// <param name="remoteAxiomId">The publisher's own <c>owl:Axiom</c> identity.</param>
    /// <param name="sourceObservationId">
    /// The custody coordinate for the observation this edge came from. Required, so a live run can
    /// always say which observation produced an edge.
    /// </param>
    public static EuLocatedAmendmentAxiom Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        TargetBodyScope targetBodyScope,
        string rawLocation,
        string rawRole,
        string? rawStartOfValidity,
        string? rawEndOfValidity,
        string typeOfLinkTarget,
        string remoteAxiomId,
        string sourceObservationId)
    {
        ArgumentNullException.ThrowIfNull(rawLocation);
        ArgumentNullException.ThrowIfNull(rawRole);

        var location = EuStructuralLocation.Parse(
            rawLocation,
            EuAmendmentRelationVocabulary.LocationAuthorityListUri);

        var roleTokens = EuStructuralLocation.Parse(
            rawRole,
            EuAmendmentRelationVocabulary.RoleAuthorityListUri);
        if (roleTokens.Tokens.Count != 1)
        {
            throw new ArgumentException(
                $"role2 carries exactly one authority-qualified token; "
                    + $"\"{EuAuthorityQualifiedToken.Describe(rawRole)}\" carries "
                    + $"{roleTokens.Tokens.Count}.",
                nameof(rawRole));
        }

        var role = roleTokens.Tokens[0];
        if (role.Value is not null)
        {
            throw new ArgumentException(
                $"role2 carries a bare code with no trailing value; "
                    + $"\"{EuAuthorityQualifiedToken.Describe(rawRole)}\" trails "
                    + $"\"{EuAuthorityQualifiedToken.Describe(role.Value)}\".",
                nameof(rawRole));
        }

        var start = rawStartOfValidity is null ? null : EuValidityDate.Create(rawStartOfValidity);
        var end = rawEndOfValidity is null ? null : EuValidityDate.Create(rawEndOfValidity);
        var linkTargetType = EuAmendmentRelationVocabulary.RequireLinkTargetType(
            typeOfLinkTarget,
            nameof(typeOfLinkTarget));

        var qualifiers = new List<AxiomQualifier>
        {
            new(EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri, location.RawValue),
        };
        if (start is not null)
        {
            qualifiers.Add(new AxiomQualifier(
                EuAmendmentRelationVocabulary.StartOfValidityUri,
                start.RawLexicalValue));
        }

        if (end is not null)
        {
            qualifiers.Add(new AxiomQualifier(
                EuAmendmentRelationVocabulary.EndOfValidityUri,
                end.RawLexicalValue));
        }

        qualifiers.Add(new AxiomQualifier(
            EuAmendmentRelationVocabulary.TypeOfLinkTargetUri,
            linkTargetType));
        qualifiers.Add(new AxiomQualifier(
            EuAmendmentRelationVocabulary.Role2Uri,
            roleTokens.RawValue));

        var axiom = new QualifiedAxiom(remoteAxiomId, qualifiers);
        var edge = EuRelationEdgeBinding.Create(
            source,
            target,
            EuAmendmentRelationVocabulary.AmendsPredicateUri,
            targetBodyScope,
            new[] { axiom },
            sourceObservationId);

        return new EuLocatedAmendmentAxiom(edge, location, role, start, end, linkTargetType, axiom);
    }
}
