using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What one delivered batch of OpinionRequests is entitled to conclude.
/// </summary>
/// <remarks>
/// The matrix is where a delivery stops being rows and starts being facts, absences and gaps. These
/// cases are about the three ways that goes wrong: concluding an absence the delivery did not earn,
/// losing a delivered row between the page and the matrix, and letting a caller state a role the
/// publisher never stated.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestCoverageTests
{
    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string RdfType = LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri;

    private const string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    private static readonly IReadOnlyList<string> Asked =
        LuxembourgOpinionRequestGraphDiscoveryPlan.AskedAbout;

    /// <summary>
    /// A typed subject that delivered a value keeps it; its typed silent siblings become absences.
    /// </summary>
    [TestMethod]
    public void ATypedDeliveryCompletesIntoValuesAndDerivedAbsences()
    {
        var requests = Subjects(3);
        var assignment = Assignment(requests);
        var present = new[] { Value(requests[0], "2004-03-11") };
        var retained = requests.Select(TypeRow).ToArray();

        var coverage = Complete(assignment, present, retained, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(3, coverage.CoveredPairCount);
        Assert.AreEqual(1, coverage.PresentPairCount);
        Assert.AreEqual(2, coverage.DerivedAbsences.Count, "the two typed silent subjects.");
        Assert.AreEqual(0, coverage.UnresolvedGaps.Count);
        Assert.AreEqual(0, coverage.RequestsOfUnconfirmedRole.Count);
        Assert.AreEqual(
            LuxembourgOpinionRequestAbsenceReason.TypedByThePublisherEnumeratedAndNotHeld,
            coverage.DerivedAbsences[0].Reason);
        CollectionAssert.AreEqual(
            new[] { requests[1], requests[2] },
            coverage.DerivedAbsences.Select(static value => value.RequestIri).ToArray(),
            "absences are emitted in the batch's own order, so two runs produce the same records.");
    }

    /// <summary>
    /// Without the publisher's own type row, silence is a gap and never an absence.
    /// </summary>
    /// <remarks>
    /// THE RULE THIS FAMILY EXISTS FOR. The query filters on the class, so every delivered row came
    /// from a subject that matched it - and the plan refuses to read that filter as the publisher
    /// having answered. A subject whose type row never arrived is silent in exactly the same way as
    /// one that holds no referral date, and deriving an absence there is the false absence S2-A03
    /// forbids.
    /// </remarks>
    [TestMethod]
    public void SilenceOverAnUntypedSubjectIsAGapAndNeverAnAbsence()
    {
        var requests = Subjects(2);
        var assignment = Assignment(requests);
        var retained = new[] { TypeRow(requests[0]) };

        var coverage = Complete(assignment, [], retained, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(1, coverage.DerivedAbsences.Count, "only the typed subject earns one.");
        Assert.AreEqual(requests[0], coverage.DerivedAbsences[0].RequestIri);
        Assert.AreEqual(1, coverage.UnresolvedGaps.Count);
        Assert.AreEqual(requests[1], coverage.UnresolvedGaps[0].RequestIri);
        Assert.AreEqual(
            LuxembourgOpinionRequestGapReason.RequestRoleNotConfirmedByDelivery,
            coverage.UnresolvedGaps[0].Reason);
        CollectionAssert.AreEqual(
            new[] { requests[1] }, coverage.RequestsOfUnconfirmedRole.ToArray());
        Assert.AreEqual(2, coverage.CoveredPairCount, "and every pair is still accounted for.");
    }

    /// <summary>A type row naming another class answers the question the other way.</summary>
    [TestMethod]
    public void ATypeRowNamingAnotherClassConfirmsNothing()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var retained = new[]
        {
            new LuxembourgOpinionRequestRecordView(
                requests[0], RdfType, LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri, IriKind),
        };

        var coverage = Complete(assignment, [], retained, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(0, coverage.DerivedAbsences.Count, "a different class is not this role.");
        Assert.AreEqual(1, coverage.UnresolvedGaps.Count);
    }

    /// <summary>
    /// A type row delivered as a literal confirms nothing, whatever its text says.
    /// </summary>
    /// <remarks>
    /// The value has to BE the class, not spell it. A literal carrying the class IRI as text is a
    /// different triple from one whose object is that resource, and admitting it would let a string
    /// comparison stand in for the publisher having typed the subject.
    /// </remarks>
    [TestMethod]
    public void ATypeRowDeliveredAsALiteralConfirmsNothing()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var retained = new[]
        {
            new LuxembourgOpinionRequestRecordView(requests[0], RdfType, RequestClass, "literal"),
        };

        var coverage = Complete(assignment, [], retained, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(0, coverage.DerivedAbsences.Count);
        Assert.AreEqual(1, coverage.UnresolvedGaps.Count);
    }

    /// <summary>
    /// An untyped subject that delivered a value keeps the value and earns no absence.
    /// </summary>
    /// <remarks>
    /// THE CASE THE DRAFT FAMILY'S ARITHMETIC CANNOT EXPRESS. There a subject is confirmed by
    /// delivering any row, so an unconfirmed one has no present pairs and its pairs are accounted by
    /// multiplying. Here confirmation comes from a PARTICULAR row, so this subject is both present
    /// and unconfirmed - and the multiplication would count its one pair twice and refuse an honest
    /// delivery.
    /// </remarks>
    [TestMethod]
    public void AnUntypedSubjectKeepsItsDeliveredValueAndIsCountedOnce()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var present = new[] { Value(requests[0], "2004-03-11") };

        var coverage = Complete(assignment, present, [], out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(1, coverage.PresentPairCount);
        Assert.AreEqual(0, coverage.DerivedAbsences.Count, "no silence to read, and no role either.");
        Assert.AreEqual(0, coverage.UnresolvedGaps.Count, "the pair is settled by its value.");
        CollectionAssert.AreEqual(
            new[] { requests[0] }, coverage.RequestsOfUnconfirmedRole.ToArray(),
            "the role is still unconfirmed, and a traversal step must be told so.");
        Assert.IsFalse(coverage.RoleConfirmedFor(requests[0]));
    }

    /// <summary>Every delivered value of one pair is kept; rows are never read as pairs.</summary>
    [TestMethod]
    public void EveryValueOfAMultiValuedPairIsKept()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var present = new[] { Value(requests[0], "2004-03-11"), Value(requests[0], "2004-04-01") };
        var retained = new[] { TypeRow(requests[0]) };

        var coverage = Complete(assignment, present, retained, out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(1, coverage.PresentPairCount, "one pair...");
        Assert.AreEqual(2, coverage.AdmittedRowCount, "...delivered twice.");
        Assert.AreEqual(2, coverage.ValuesFor(requests[0], ReferralDate).Count);
        Assert.IsNull(
            coverage.DerivedAbsenceFor(requests[0], ReferralDate),
            "a pair with values is not an absence.");
    }

    /// <summary>
    /// A row that is neither admitted nor retained refuses the matrix.
    /// </summary>
    /// <remarks>
    /// WHOLE-DELIVERY CONSERVATION. The acquisition asks for every predicate the publisher holds, so
    /// a row dropped between the page and this matrix is invisible in every count below - including
    /// a type row, whose loss silently turns absences into gaps.
    /// </remarks>
    [TestMethod]
    public void ARowThatIsNeitherAdmittedNorRetainedRefusesTheMatrix()
    {
        var requests = Subjects(2);
        var assignment = Assignment(requests);
        var retained = new[] { TypeRow(requests[0]), TypeRow(requests[1]) };

        // The citation is minted over a delivery of three rows; only two are accounted for.
        var citation = Citation(assignment, deliveredRowCount: 3);
        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            assignment, Asked, [], retained, citation, out var refusal, out _);

        Assert.IsNull(coverage);
        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotAccountedExactlyOnce, refusal);
    }

    /// <summary>
    /// A retained row carrying an admitted predicate means the delivery was split wrongly.
    /// </summary>
    /// <remarks>
    /// Conservation alone would stay balanced: the row is counted, so the totals agree while the
    /// matrix never sees the value and derives an absence beside it.
    /// </remarks>
    [TestMethod]
    public void ARetainedRowCarryingAnAdmittedPredicateIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var retained = new[] { Value(requests[0], "2004-03-11") };

        Complete(assignment, [], retained, out var refusal, out _);

        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.RetainedRowCarriesAnAdmissiblePredicate, refusal);
    }

    /// <summary>An admitted row about a subject this batch never asked about is refused.</summary>
    [TestMethod]
    public void AnAdmittedRowNamingAnUnrequestedSubjectIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var present = new[] { Value(Request(97), "2004-03-11") };

        Complete(assignment, present, [], out var refusal, out _);

        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested, refusal);
    }

    /// <summary>
    /// A retained row about a subject this batch never asked about is refused too.
    /// </summary>
    /// <remarks>
    /// The retained half is where the roles are read from, so a stray subject there is not harmless
    /// bookkeeping: it is a delivery that is not this batch's being used to type this batch's
    /// members.
    /// </remarks>
    [TestMethod]
    public void ARetainedRowNamingAnUnrequestedSubjectIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var retained = new[] { TypeRow(requests[0]), TypeRow(Request(97)) };

        Complete(assignment, [], retained, out var refusal, out _);

        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested, refusal);
    }

    /// <summary>An admitted row carrying a predicate this family never asked about is refused.</summary>
    [TestMethod]
    public void AnAdmittedRowCarryingAnUnaskedPredicateIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var present = new[]
        {
            new LuxembourgOpinionRequestRecordView(requests[0], RdfType, RequestClass, IriKind),
        };

        Complete(assignment, present, [], out var refusal, out _);

        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredPredicateNotAskedAbout, refusal);
    }

    /// <summary>Without a citation there is no proven enumeration, so there is no matrix.</summary>
    [TestMethod]
    public void AMatrixCannotBeCompletedWithoutTheEnumerationThatProvesIt()
    {
        var assignment = Assignment(Subjects(1));

        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            assignment, Asked, [], [], null, out var refusal, out _);

        Assert.IsNull(coverage);
        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.MatrixCompletionOverUnprovenEnumeration, refusal);
    }

    /// <summary>A citation minted for another batch does not complete this one.</summary>
    [TestMethod]
    public void ACitationFromAnotherBatchDoesNotCompleteThisOne()
    {
        var population = Subjects(LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity + 1);
        var inventory = Inventory(population);
        var batches = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory);

        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            batches[0], Asked, [], [], Citation(batches[1], 1), out var refusal, out _);

        Assert.IsNull(coverage);
        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.RequestedBatchNotRetained, refusal);
    }

    /// <summary>
    /// Nothing can be read out of the matrix about a question this batch never asked.
    /// </summary>
    [TestMethod]
    public void TheMatrixRefusesToAnswerForSubjectsAndPropertiesOutsideIt()
    {
        var requests = Subjects(1);
        var coverage = Complete(
            Assignment(requests), [], [TypeRow(requests[0])], out var refusal, out var detail);

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.ThrowsExactly<ArgumentException>(
            () => coverage.ValuesFor(Request(97), ReferralDate),
            "an empty value list would read as the publisher holding nothing.");
        Assert.ThrowsExactly<ArgumentException>(
            () => coverage.ValuesFor(requests[0], RdfType),
            "this family asked about one property, and says nothing about the others.");
        Assert.ThrowsExactly<ArgumentException>(
            () => coverage.RoleConfirmedFor(Request(97)),
            "a false role reading is as bad as a false absence.");
    }

    /// <summary>
    /// The IRI marker this file reads roles by is the one the query itself binds.
    /// </summary>
    /// <remarks>
    /// The role check compares a delivered value kind against a constant, and the query's own BIND
    /// is what produces that kind. Pinned against the rendered template rather than against another
    /// constant, because two constants agreeing with each other proves nothing about the text that
    /// has to produce the value.
    /// </remarks>
    [TestMethod]
    public void TheRoleMarkerIsTheOneTheRenderedQueryBinds()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();

        StringAssert.Contains(
            plan.PageTemplate,
            $"IF(isIRI(?value), \"{IriKind}\"",
            "a role read under a marker the query never binds is never confirmed.");
    }

    private static LuxembourgOpinionRequestCoverage? Complete(
        LuxembourgOpinionRequestBatchAssignment assignment,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> present,
        IReadOnlyList<LuxembourgOpinionRequestRecordView> retained,
        out LuxembourgOpinionRequestCoverageRefusal refusal,
        out string? detail) =>
        LuxembourgOpinionRequestCoverage.TryComplete(
            assignment,
            Asked,
            present,
            retained,
            Citation(assignment, present.Count + retained.Count),
            out refusal,
            out detail);

    private static LuxembourgOpinionRequestRecordView Value(string request, string value) =>
        new(request, ReferralDate, value, "literal");

    private static LuxembourgOpinionRequestRecordView TypeRow(string request) =>
        new(request, RdfType, RequestClass, IriKind);

    /// <summary>
    /// A batch citation over a delivery of exactly this many rows.
    /// </summary>
    /// <remarks>
    /// The rows carry tokens rather than the matrix's own values: what this citation contributes to
    /// a matrix is its selection, its partition and its delivered row COUNT. That the rows are the
    /// proof's own, and that the delivery was read under this family's graph profile, are the batch
    /// citation's own rules, exercised beside it.
    /// </remarks>
    private static LuxembourgOpinionRequestBatchCitation Citation(
        LuxembourgOpinionRequestBatchAssignment assignment,
        int deliveredRowCount)
    {
        var tokens = Enumerable.Range(0, deliveredRowCount)
            .Select(index => $"row-{index:D3}")
            .ToArray();
        var (proof, keys) = AbsenceFixtures.OpinionRequestGraphBatchDelivery(
            assignment.PartitionKey, tokens);
        var rows = tokens
            .Select((token, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + token)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + token)]))
            .ToArray();
        return LuxembourgOpinionRequestBatchCitation.ForDelivery(proof, rows, assignment);
    }

    private static LuxembourgOpinionRequestBatchAssignment Assignment(IReadOnlyList<string> requests) =>
        LuxembourgOpinionRequestBatchAssignment.Over(requests, Inventory(requests))[0];

    private static LuxembourgOpinionRequestInventoryCitation Inventory(IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static IReadOnlyList<string> Subjects(int count) =>
        Enumerable.Range(0, count).Select(Request)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D3}/evenement/sace/1";
}
