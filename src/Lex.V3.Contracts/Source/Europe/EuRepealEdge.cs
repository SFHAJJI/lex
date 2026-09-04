using Lex.V3.Contracts.Facts;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// A repeal axiom: one <c>cdm:resource_legal_repeals_resource_legal</c> edge with the annotations
/// the publisher reified onto it.
/// </summary>
/// <remarks>
/// <para>
/// Grounded in the GDPR repeals axiom read in full at the EU SPARQL endpoint (canary
/// <c>lex-event-20260904T174651520Z-392411cf4e9446e2aa76bd3be3cc2c8a</c>, digest
/// 4701a3361ff09048): <c>start_of_validity</c> 2018-05-25, <c>type_of_link_target</c> MS,
/// <c>owl:annotatedTarget</c> cellar 775a4724, which is the 1995 Data Protection Directive. The
/// same cellar identifier was resolved independently by a separate 1995 canary the same day, so
/// the two probes cross-check each other.
/// </para>
/// <para>
/// <b>A repeal is not a located amendment, and the difference is observed rather than assumed.</b>
/// The annotation set on a repeals axiom carries <c>type_of_link_target</c> and carries no
/// <c>reference_to_modified_location</c>: a repeal removes a whole act and has no structural
/// location inside it to point at. That is why this type has no location and no fd_375 role, and
/// why it is a separate type rather than a located amendment with two null fields, which would
/// invite a reader to ask why the location is missing on every repeal ever recorded.
/// </para>
/// <para>
/// <see cref="StartOfValidity"/> and <see cref="EndOfValidity"/> are both optional and both use
/// <see cref="EuValidityDate"/>, so a repeal inherits the same closed two-spelling rule. The one
/// retained repeals axiom carries a hyphenated start and no end. No slash-spelled date has been
/// observed on this predicate specifically; the shared type accepts one because the spelling
/// inconsistency is the publisher's and there is no evidence it is confined to one predicate.
/// </para>
/// </remarks>
public sealed class EuRepealEdge
{
    private EuRepealEdge(
        EuRelationEdge edge,
        EuValidityDate? startOfValidity,
        EuValidityDate? endOfValidity,
        string typeOfLinkTarget,
        QualifiedAxiom axiom)
    {
        Edge = edge;
        StartOfValidity = startOfValidity;
        EndOfValidity = endOfValidity;
        TypeOfLinkTarget = typeOfLinkTarget;
        Axiom = axiom;
    }

    /// <summary>
    /// The repeal edge, repealing act to repealed act, carrying the typed target state. Always
    /// publisher-materialised on <see cref="EuAmendmentRelationVocabulary.RepealsPredicateUri"/>.
    /// </summary>
    public EuRelationEdge Edge { get; }

    /// <summary>When the repeal takes effect, or <c>null</c> where the publisher bound none.</summary>
    public EuValidityDate? StartOfValidity { get; }

    /// <summary>When the repeal stops holding, or <c>null</c>.</summary>
    public EuValidityDate? EndOfValidity { get; }

    /// <summary>The link target type exactly as observed. <c>MS</c> on the retained axiom.</summary>
    public string TypeOfLinkTarget { get; }

    /// <summary>The same annotations as a Facts-layer axiom, carrying the publisher's raw values.</summary>
    public QualifiedAxiom Axiom { get; }

    /// <summary>Reads one repeal axiom from the publisher's raw annotation values.</summary>
    /// <param name="source">The repealing act.</param>
    /// <param name="target">The repealed act.</param>
    /// <param name="targetState">How <paramref name="target"/> stands.</param>
    /// <param name="rawStartOfValidity">The <c>start_of_validity</c> value, or null when unbound.</param>
    /// <param name="rawEndOfValidity">The <c>end_of_validity</c> value, or null when unbound.</param>
    /// <param name="typeOfLinkTarget">The <c>type_of_link_target</c> value, verbatim.</param>
    /// <param name="remoteAxiomId">The publisher's own <c>owl:Axiom</c> identity.</param>
    public static EuRepealEdge Create(
        OfficialIdentitySet source,
        OfficialIdentitySet target,
        EuRelationTargetState targetState,
        string? rawStartOfValidity,
        string? rawEndOfValidity,
        string typeOfLinkTarget,
        string remoteAxiomId)
    {
        var start = rawStartOfValidity is null ? null : EuValidityDate.Create(rawStartOfValidity);
        var end = rawEndOfValidity is null ? null : EuValidityDate.Create(rawEndOfValidity);
        var linkTargetType = EuAmendmentRelationVocabulary.RequireLinkTargetType(
            typeOfLinkTarget,
            nameof(typeOfLinkTarget));

        var qualifiers = new List<AxiomQualifier>();
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

        var axiom = new QualifiedAxiom(remoteAxiomId, qualifiers);
        var edge = EuRelationEdge.Create(
            source,
            target,
            EuAmendmentRelationVocabulary.RepealsPredicateUri,
            EuRelationMaterialisation.PublisherMaterialised,
            invertedFromPredicateUri: null,
            targetState,
            new[] { axiom });

        return new EuRepealEdge(edge, start, end, linkTargetType, axiom);
    }
}
