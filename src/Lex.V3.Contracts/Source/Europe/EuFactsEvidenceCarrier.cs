namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Marks a Facts-layer EU binding as admissible evidence for a legal record: implemented by E1's
/// <see cref="EuDateAxiomBinding"/> and E6's <see cref="EuCaseLawLinkBinding"/>, and by nothing
/// else in this assembly. A pure marker: it declares no members, so a type opts in only by
/// naming the interface on its own declaration, never by accident through structural typing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every future evidence bundle demands this marker.</b> A bundle admitting EU Facts-layer
/// evidence must type its members as <see cref="IEuFactsEvidenceCarrier"/>, never as a concrete
/// binding type directly. A member typed this way does not "carry" any one concrete binding
/// under <c>Lex.V3.TestSupport.ConstructionSurface.Carries</c> (which tests exact type or
/// subtype of the guarded type, never a supertype it happens to implement), so a bundle built
/// this way trips neither
/// <c>EuDateAxiomTests.EveryOtherProducerOfABindingInTheAssemblyIsExactlyTheClassificationsOwnHolder</c>
/// nor <c>EuCaseLawLinkTests.NoOtherTypeInTheAssemblyHoldsOrProducesABinding</c>. This is what
/// makes a summary record's exclusion from evidence bundles (see the remarks on
/// <see cref="EuLegislationSummary"/>) a structural fact rather than a documented convention:
/// <see cref="EuLegislationSummary"/> does not implement this interface, so it can never be an
/// element of any collection correctly typed against it, and
/// <c>EuLegislationSummaryTests</c> pins the closed set of implementers reflectively so a type
/// added here later cannot silently widen it.
/// </para>
/// </remarks>
public interface IEuFactsEvidenceCarrier
{
}
