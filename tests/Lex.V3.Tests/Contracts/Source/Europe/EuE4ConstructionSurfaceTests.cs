using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The construction surface of every public type stage 2 item E4 declares, plus the assembly sweep
/// for each, as E6's <c>EuCaseLawLinkTests</c> already does for its own binding.
/// </summary>
/// <remarks>
/// <para>
/// Required by the design verdict
/// <c>lex-event-20260904T192820932Z-4101310a2b7a482d87330f1eda1ec14a</c>. The reason is not
/// symmetry with E6: a validated type is only worth as much as the set of doors that can mint one,
/// and visibility alone does not bound that set. A second producer added later, anywhere in the
/// assembly, would let a caller build an E4 value that skipped the checks its named factory
/// performs, and nothing else in this suite would notice. Two lanes found gate holes of exactly
/// this shape on the same day.
/// </para>
/// <para>
/// Every expected array below is transcribed from <see cref="ConstructionSurface"/>'s actual
/// output, following this project's print-then-transcribe technique, rather than assembled from
/// the same reflection the guard uses.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuE4ConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";
    private const string F = "Lex.V3.Contracts.Facts.";
    private const string G = "System.Collections.Generic.";

    // --- The asserted binding -------------------------------------------------------------------

    /// <summary>
    /// <see cref="EuRelationEdgeBinding.Create"/> is the only door. It is the one place that pins
    /// the predicate against the closed set, requires the observation id, and computes the ECLI
    /// state, so a second producer would be a way to get a fact without any of the three.
    /// </summary>
    [TestMethod]
    public void TheAssertedBindingIsMintedByExactlyCreateAndItsPrivateConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuRelationEdgeBinding::.ctor(" + F
                    + "RelationFact) -> " + N + "EuRelationEdgeBinding",
                "method public static " + N + "EuRelationEdgeBinding::Create(" + F
                    + "OfficialIdentitySet, " + F + "OfficialIdentitySet, System.String, " + F
                    + "TargetBodyScope, " + G + "IReadOnlyList<" + F + "QualifiedAxiom>, "
                    + "System.String) -> " + N + "EuRelationEdgeBinding",
            },
            ConstructionSurface.Of(typeof(EuRelationEdgeBinding)).ToArray());
    }

    /// <summary>
    /// Nothing else in the assembly produces a binding. The two holders are the located amendment
    /// axiom and the repeal edge, which hold one rather than mint one.
    /// </summary>
    [TestMethod]
    public void TheOnlyOtherPlacesABindingAppearsAreTheTwoTypesThatHoldOne()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuLocatedAmendmentAxiom::<Edge>k__BackingField -> "
                    + N + "EuRelationEdgeBinding",
                "field private instance " + N + "EuRepealEdge::<Edge>k__BackingField -> " + N
                    + "EuRelationEdgeBinding",
                "property public instance " + N + "EuLocatedAmendmentAxiom::Edge() -> " + N
                    + "EuRelationEdgeBinding",
                "property public instance " + N + "EuRepealEdge::Edge() -> " + N
                    + "EuRelationEdgeBinding",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuRelationEdgeBinding).Assembly, typeof(EuRelationEdgeBinding), true).ToArray());
    }

    // --- The derived inverse --------------------------------------------------------------------

    /// <summary>
    /// Exactly one door in this assembly mints a <see cref="DerivedInverseRelation"/>, and it is
    /// E4's. That matters more than the usual pin: a second producer could hand out an inverse
    /// authorised by an axiom nobody observed, which is the invention
    /// <see cref="ObservedInverseAxiom"/> exists to prevent.
    /// </summary>
    /// <remarks>
    /// The two <c>RelationFact</c> entries are that record's own field and property for the
    /// inverse it may carry; they hold one rather than mint one.
    /// </remarks>
    [TestMethod]
    public void ExactlyOneDoorInTheAssemblyMintsADerivedInverseRelation()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + F
                    + "RelationFact::<OntologyAuthorizedInverse>k__BackingField -> " + F
                    + "DerivedInverseRelation?",
                "method public static " + N + "EuDerivedAmendmentInverse::From(" + N
                    + "EuRelationEdgeBinding, System.String) -> " + F + "DerivedInverseRelation",
                "property public instance " + F + "RelationFact::OntologyAuthorizedInverse() -> "
                    + F + "DerivedInverseRelation?",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuRelationEdgeBinding).Assembly, typeof(DerivedInverseRelation), true).ToArray());
    }

    /// <summary>
    /// The derivation helper is a static class, so nothing mints one of it. Pinned as empty so
    /// that turning it into an instantiable type shows up here.
    /// </summary>
    [TestMethod]
    public void TheDerivationHelperIsNotItselfConstructible()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(EuDerivedAmendmentInverse)).ToArray());
    }

    // --- The two qualifier-carrying facts -------------------------------------------------------

    [TestMethod]
    public void TheLocatedAmendmentAxiomIsMintedByExactlyCreateAndNothingHoldsOne()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuLocatedAmendmentAxiom::.ctor(" + N
                    + "EuRelationEdgeBinding, " + N + "EuStructuralLocation, " + N
                    + "EuAuthorityQualifiedToken, " + N + "EuValidityDate?, " + N
                    + "EuValidityDate?, System.String, " + F + "QualifiedAxiom) -> " + N
                    + "EuLocatedAmendmentAxiom",
                "method public static " + N + "EuLocatedAmendmentAxiom::Create(" + F
                    + "OfficialIdentitySet, " + F + "OfficialIdentitySet, " + F
                    + "TargetBodyScope, System.String, System.String, System.String?, "
                    + "System.String?, System.String, System.String, System.String) -> " + N
                    + "EuLocatedAmendmentAxiom",
            },
            ConstructionSurface.Of(typeof(EuLocatedAmendmentAxiom)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuLocatedAmendmentAxiom).Assembly,
                typeof(EuLocatedAmendmentAxiom),
                true).ToArray());
    }

    [TestMethod]
    public void TheRepealEdgeIsMintedByExactlyCreateAndNothingHoldsOne()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuRepealEdge::.ctor(" + N
                    + "EuRelationEdgeBinding, " + N + "EuValidityDate?, " + N
                    + "EuValidityDate?, System.String, " + F + "QualifiedAxiom) -> " + N
                    + "EuRepealEdge",
                "method public static " + N + "EuRepealEdge::Create(" + F + "OfficialIdentitySet, "
                    + F + "OfficialIdentitySet, " + F + "TargetBodyScope, System.String?, "
                    + "System.String?, System.String, System.String, System.String) -> " + N
                    + "EuRepealEdge",
            },
            ConstructionSurface.Of(typeof(EuRepealEdge)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuRepealEdge).Assembly, typeof(EuRepealEdge), true).ToArray());
    }

    // --- The qualifier layer --------------------------------------------------------------------

    /// <summary>
    /// <see cref="EuStructuralLocation.Parse"/> is the only door, and it always takes the expected
    /// authority list explicitly. The convenience overload that defaulted that argument to fd_370
    /// is gone: it was unused, and a default authority is exactly the thing a caller should have to
    /// state.
    /// </summary>
    [TestMethod]
    public void TheStructuralLocationIsMintedByExactlyTheTwoArgumentParse()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuStructuralLocation::.ctor(System.String, "
                    + G + "IReadOnlyList<" + N + "EuAuthorityQualifiedToken>) -> " + N
                    + "EuStructuralLocation",
                "method public static " + N
                    + "EuStructuralLocation::Parse(System.String, System.String) -> " + N
                    + "EuStructuralLocation",
            },
            ConstructionSurface.Of(typeof(EuStructuralLocation)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N
                    + "EuLocatedAmendmentAxiom::<Location>k__BackingField -> " + N
                    + "EuStructuralLocation",
                "property public instance " + N + "EuLocatedAmendmentAxiom::Location() -> " + N
                    + "EuStructuralLocation",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuStructuralLocation).Assembly, typeof(EuStructuralLocation), true).ToArray());
    }

    [TestMethod]
    public void TheAuthorityQualifiedTokenIsMintedByExactlyCreate()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N
                    + "EuAuthorityQualifiedToken::.ctor(System.String, System.String, "
                    + "System.String?) -> " + N + "EuAuthorityQualifiedToken",
                "method public static " + N
                    + "EuAuthorityQualifiedToken::Create(System.String, System.String, "
                    + "System.String?, System.String) -> " + N + "EuAuthorityQualifiedToken",
            },
            ConstructionSurface.Of(typeof(EuAuthorityQualifiedToken)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuLocatedAmendmentAxiom::<Role>k__BackingField -> "
                    + N + "EuAuthorityQualifiedToken",
                "field private instance " + N + "EuStructuralLocation::<Tokens>k__BackingField -> "
                    + G + "IReadOnlyList<" + N + "EuAuthorityQualifiedToken>",
                "property public instance " + N + "EuLocatedAmendmentAxiom::Role() -> " + N
                    + "EuAuthorityQualifiedToken",
                "property public instance " + N + "EuStructuralLocation::Tokens() -> " + G
                    + "IReadOnlyList<" + N + "EuAuthorityQualifiedToken>",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuAuthorityQualifiedToken).Assembly,
                typeof(EuAuthorityQualifiedToken),
                true).ToArray());
    }

    [TestMethod]
    public void TheValidityDateIsMintedByExactlyCreate()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuValidityDate::.ctor(System.String, " + N
                    + "EuValidityDateShape, " + F + "PublisherDate?) -> " + N + "EuValidityDate",
                "method public static " + N + "EuValidityDate::Create(System.String) -> " + N
                    + "EuValidityDate",
            },
            ConstructionSurface.Of(typeof(EuValidityDate)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N
                    + "EuLocatedAmendmentAxiom::<EndOfValidity>k__BackingField -> " + N
                    + "EuValidityDate?",
                "field private instance " + N
                    + "EuLocatedAmendmentAxiom::<StartOfValidity>k__BackingField -> " + N
                    + "EuValidityDate?",
                "field private instance " + N + "EuRepealEdge::<EndOfValidity>k__BackingField -> "
                    + N + "EuValidityDate?",
                "field private instance " + N + "EuRepealEdge::<StartOfValidity>k__BackingField -> "
                    + N + "EuValidityDate?",
                "property public instance " + N + "EuLocatedAmendmentAxiom::EndOfValidity() -> " + N
                    + "EuValidityDate?",
                "property public instance " + N + "EuLocatedAmendmentAxiom::StartOfValidity() -> "
                    + N + "EuValidityDate?",
                "property public instance " + N + "EuRepealEdge::EndOfValidity() -> " + N
                    + "EuValidityDate?",
                "property public instance " + N + "EuRepealEdge::StartOfValidity() -> " + N
                    + "EuValidityDate?",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuValidityDate).Assembly, typeof(EuValidityDate), true).ToArray());
    }

    // --- The closure ----------------------------------------------------------------------------

    /// <summary>
    /// <see cref="EuConstituentClosure.Validate"/> is the only public door. A second producer could
    /// hand out a closure whose chain was never checked, which is the whole failure this type
    /// exists to prevent.
    /// </summary>
    [TestMethod]
    public void TheClosureIsMintedByExactlyValidateAndItsPrivateRefusalPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                // The two nullable parameters are the refusal path: a refused closure is built with
                // no chain and a detail string, and a validated one with a chain and no detail.
                "constructor private instance " + N + "EuConstituentClosure::.ctor(" + F
                    + "OfficialIdentitySet, " + G + "IReadOnlyList<" + N + "EuConstituentStep>?, "
                    + N + "EuConstituentClosureRefusal, System.String?) -> " + N
                    + "EuConstituentClosure",
                "method private static " + N + "EuConstituentClosure::Refuse(" + F
                    + "OfficialIdentitySet, " + N + "EuConstituentClosureRefusal, System.String) -> "
                    + N + "EuConstituentClosure",
                "method public static " + N + "EuConstituentClosure::Validate(" + F
                    + "OfficialIdentitySet, " + G + "IReadOnlyList<" + N + "EuConstituentStep>) -> "
                    + N + "EuConstituentClosure",
            },
            ConstructionSurface.Of(typeof(EuConstituentClosure)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuConstituentClosure).Assembly, typeof(EuConstituentClosure), true).ToArray());
    }

    [TestMethod]
    public void TheClosureStepIsMintedByExactlyCreate()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuConstituentStep::.ctor(" + F
                    + "OfficialIdentitySet, " + F + "OfficialIdentitySet, " + F
                    + "OfficialIdentitySet, " + N + "EuConstituentMemberResolution) -> " + N
                    + "EuConstituentStep",
                "method public static " + N + "EuConstituentStep::Create(" + F
                    + "OfficialIdentitySet, " + F + "OfficialIdentitySet, System.String, " + F
                    + "OfficialIdentitySet, System.String, " + N
                    + "EuConstituentMemberResolution) -> " + N + "EuConstituentStep",
            },
            ConstructionSurface.Of(typeof(EuConstituentStep)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuConstituentClosure::_chain -> " + G
                    + "IReadOnlyList<" + N + "EuConstituentStep>?",
                "property public instance " + N + "EuConstituentClosure::Chain() -> " + G
                    + "IReadOnlyList<" + N + "EuConstituentStep>",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuConstituentStep).Assembly, typeof(EuConstituentStep), true).ToArray());
    }

    // --- The three enums ------------------------------------------------------------------------

    /// <summary>
    /// Each E4 enum exposes exactly its declared members and nothing else in the assembly hands one
    /// out beyond the property that carries it. A member added later changes these arrays, so the
    /// closed sets cannot widen silently.
    /// </summary>
    [TestMethod]
    public void TheValidityDateShapeExposesExactlyItsTwoObservedSpellings()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuValidityDateShape::HyphenatedIso8601 -> " + N
                    + "EuValidityDateShape",
                "field public static " + N + "EuValidityDateShape::SlashSeparated -> " + N
                    + "EuValidityDateShape",
            },
            ConstructionSurface.Of(typeof(EuValidityDateShape)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuValidityDate::<ObservedShape>k__BackingField -> "
                    + N + "EuValidityDateShape",
                "method private static " + N + "EuValidityDate::ClassifyOrRefuse(System.String) -> "
                    + N + "EuValidityDateShape",
                "property public instance " + N + "EuValidityDate::ObservedShape() -> " + N
                    + "EuValidityDateShape",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuValidityDate).Assembly, typeof(EuValidityDateShape), true).ToArray());
    }

    [TestMethod]
    public void TheClosureRefusalExposesExactlyR4sFourCasesPlusNone()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuConstituentClosureRefusal::CrossRootMember -> " + N
                    + "EuConstituentClosureRefusal",
                "field public static " + N + "EuConstituentClosureRefusal::CyclicChain -> " + N
                    + "EuConstituentClosureRefusal",
                "field public static " + N + "EuConstituentClosureRefusal::None -> " + N
                    + "EuConstituentClosureRefusal",
                "field public static " + N + "EuConstituentClosureRefusal::UnexplainedMismatch -> "
                    + N + "EuConstituentClosureRefusal",
                "field public static " + N + "EuConstituentClosureRefusal::UnresolvedMember -> " + N
                    + "EuConstituentClosureRefusal",
            },
            ConstructionSurface.Of(typeof(EuConstituentClosureRefusal)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuConstituentClosure::<Refusal>k__BackingField -> "
                    + N + "EuConstituentClosureRefusal",
                "property public instance " + N + "EuConstituentClosure::Refusal() -> " + N
                    + "EuConstituentClosureRefusal",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuConstituentClosure).Assembly,
                typeof(EuConstituentClosureRefusal),
                true).ToArray());
    }

    [TestMethod]
    public void TheMemberResolutionExposesExactlyItsTwoAnswers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N + "EuConstituentMemberResolution::Resolved -> " + N
                    + "EuConstituentMemberResolution",
                "field public static " + N + "EuConstituentMemberResolution::Unresolved -> " + N
                    + "EuConstituentMemberResolution",
            },
            ConstructionSurface.Of(typeof(EuConstituentMemberResolution)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuConstituentStep::<Resolution>k__BackingField -> "
                    + N + "EuConstituentMemberResolution",
                "property public instance " + N + "EuConstituentStep::Resolution() -> " + N
                    + "EuConstituentMemberResolution",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuConstituentStep).Assembly,
                typeof(EuConstituentMemberResolution),
                true).ToArray());
    }
}
