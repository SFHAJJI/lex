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
/// cases are about the ways that goes wrong: concluding an absence the delivery did not earn,
/// losing a delivered row between the page and the matrix, and reading a row that says something
/// its own proof never covered.
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

    /// <summary>
    /// A typed subject that delivered a value keeps it; its typed silent siblings become absences.
    /// </summary>
    [TestMethod]
    public void ATypedDeliveryCompletesIntoValuesAndDerivedAbsences()
    {
        var requests = Subjects(3);
        var assignment = Assignment(requests);

        var coverage = Complete(
            assignment,
            out var refusal,
            out var detail,
            TypeRow(requests[0]),
            TypeRow(requests[1]),
            TypeRow(requests[2]),
            Value(requests[0], "2004-03-11"));

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(3, coverage.CoveredPairCount);
        Assert.AreEqual(1, coverage.PresentPairCount);
        Assert.AreEqual(2, coverage.DerivedAbsences.Count, "the two typed silent subjects.");
        Assert.AreEqual(0, coverage.UnresolvedGaps.Count);
        Assert.AreEqual(0, coverage.RequestsOfUnconfirmedRole.Count);
        Assert.AreEqual(3, coverage.RetainedRows.Count, "the three type rows are retained, not admitted.");
        Assert.AreEqual(
            LuxembourgOpinionRequestAbsenceReason.TypedByThePublisherEnumeratedAndNotHeld,
            coverage.DerivedAbsences[0].Reason);
        CollectionAssert.AreEqual(
            new[] { requests[1], requests[2] },
            coverage.DerivedAbsences.Select(static value => value.RequestIri).ToArray(),
            "absences are emitted in the batch's own order, so two runs produce the same records.");
        CollectionAssert.AreEqual(
            new[] { "2004-03-11" },
            coverage.ValuesFor(requests[0], ReferralDate).Select(static v => v.Value).ToArray());
    }

    /// <summary>
    /// Without the publisher's own type row, silence is a gap and never an absence.
    /// </summary>
    /// <remarks>
    /// THE RULE THIS FAMILY EXISTS FOR. The query filters on the class, so every delivered row came
    /// from a subject that matched it - and the plan refuses to read that filter as the publisher
    /// having answered. A subject whose type row never arrived is silent in exactly the same way as
    /// one that holds no referral date.
    /// </remarks>
    [TestMethod]
    public void SilenceOverAnUntypedSubjectIsAGapAndNeverAnAbsence()
    {
        var requests = Subjects(2);
        var assignment = Assignment(requests);

        var coverage = Complete(assignment, out var refusal, out var detail, TypeRow(requests[0]));

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

        var coverage = Complete(
            assignment,
            out var refusal,
            out var detail,
            (requests[0], RdfType, LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri, true));

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
    /// different triple from one whose object is that resource.
    /// </remarks>
    [TestMethod]
    public void ATypeRowDeliveredAsALiteralConfirmsNothing()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);

        var coverage = Complete(
            assignment, out var refusal, out var detail, (requests[0], RdfType, RequestClass, false));

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

        var coverage = Complete(
            assignment, out var refusal, out var detail, Value(requests[0], "2004-03-11"));

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

        var coverage = Complete(
            assignment,
            out var refusal,
            out var detail,
            TypeRow(requests[0]),
            Value(requests[0], "2004-03-11"),
            Value(requests[0], "2004-04-01"));

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(1, coverage.PresentPairCount, "one pair...");
        Assert.AreEqual(2, coverage.AdmittedRowCount, "...delivered twice.");
        CollectionAssert.AreEquivalent(
            new[] { "2004-03-11", "2004-04-01" },
            coverage.ValuesFor(requests[0], ReferralDate).Select(static v => v.Value).ToArray());
        Assert.IsNull(
            coverage.DerivedAbsenceFor(requests[0], ReferralDate),
            "a pair with values is not an absence.");
    }

    /// <summary>
    /// A row whose terms were substituted keeps its proof and still says nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HOSTILE CASE THE KEY CHECK EXISTS FOR. An enumeration proof digests canonical KEYS, not
    /// the terms beside them, and the row list is publicly constructible. So a delivery of the right
    /// COUNT whose terms were replaced by a well-formed <c>rdf:type OpinionRequest</c> row passes
    /// every count identity: the citation binds, conservation balances, and until the terms were
    /// bound to their own keys this confirmed a role and minted an absence from evidence that never
    /// delivered that row.
    /// </para>
    /// <para>
    /// Nothing about the proof is touched here. The keys are the honest delivery's own.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ASubstitutedRowOfTheSameCountIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);

        // An honest delivery: one referral-date value, no type row, so no absence is derivable.
        var (proof, honest) = AbsenceFixtures.OpinionRequestGraphRows(
            assignment.PartitionKey, [Value(requests[0], "2004-03-11")]);

        // The forgery: a type row's terms carried on the honest row's proof-covered key.
        var forged = AbsenceFixtures.OpinionRequestGraphRows(
            assignment.PartitionKey, [TypeRow(requests[0])], runSeed: 939).Rows;
        var substituted = new[]
        {
            new RepeatedEnumerationRow(forged[0].Terms, honest[0].CanonicalKey, honest[0].Cursor),
        };

        Assert.AreEqual(honest.Length, substituted.Length, "the same count, which is the point.");

        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            proof, substituted, assignment, out var refusal, out var detail);

        Assert.IsNull(coverage, detail);
        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotDescribedByItsOwnKey, refusal);
    }

    /// <summary>
    /// A row whose value was replaced no longer digests to its own key.
    /// </summary>
    /// <remarks>
    /// The narrower half of the same attack: everything about the row is honest except the one
    /// lexical value a reader would take as the publisher's fact. <c>key_4</c> is the publisher's
    /// own digest of that value, so the substitution has nowhere to hide.
    /// </remarks>
    [TestMethod]
    public void ARowWhoseValueWasReplacedIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);
        var (proof, honest) = AbsenceFixtures.OpinionRequestGraphRows(
            assignment.PartitionKey, [TypeRow(requests[0]), Value(requests[0], "2004-03-11")]);

        var valueRow = honest.Single(row =>
            string.Equals(row.Terms[2].Value, ReferralDate, StringComparison.Ordinal));
        var rewritten = valueRow.Terms.ToArray();
        rewritten[3] = RepeatedEnumerationRdfTerm.Literal("1999-01-01", null, null);

        var substituted = honest
            .Select(row => row == valueRow
                ? new RepeatedEnumerationRow(rewritten, row.CanonicalKey, row.Cursor)
                : row)
            .ToArray();

        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            proof, substituted, assignment, out var refusal, out var detail);

        Assert.IsNull(coverage, detail);
        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRowNotDescribedByItsOwnKey, refusal);
        StringAssert.Contains(detail ?? string.Empty, "key_4");
    }

    /// <summary>
    /// The properties this family asks about come from the plan, and cannot be stated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND SUBSTITUTION THIS DOOR ONCE ALLOWED. The predicate set was a parameter checked
    /// only for non-emptiness and uniqueness, so a caller could mint a matrix and its derived
    /// absences for a property the exact request-graph plan never designated as asked about - an
    /// absence over a question nobody put to the publisher.
    /// </para>
    /// <para>
    /// It is now underivable from anything a caller holds: the set is the plan's own, and a
    /// delivered row carrying any other predicate is retained rather than admitted, so no pair and
    /// no absence can exist for it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ThePredicateSetIsThePlansAndNoOtherPredicateEntersTheMatrix()
    {
        Assert.AreSame(
            LuxembourgOpinionRequestGraphDiscoveryPlan.AskedAbout,
            LuxembourgOpinionRequestCoverage.AskedPredicates,
            "the matrix asks what the plan asked, and there is no parameter to say otherwise.");

        var requests = Subjects(1);
        var assignment = Assignment(requests);
        const string Unasked = "http://data.legilux.public.lu/resource/ontology/jolux#dateDocument";

        var coverage = Complete(
            assignment,
            out var refusal,
            out var detail,
            TypeRow(requests[0]),
            (requests[0], Unasked, "2004-03-11", false));

        Assert.AreEqual(LuxembourgOpinionRequestCoverageRefusal.None, refusal, detail);
        Assert.IsNotNull(coverage);
        Assert.AreEqual(
            1, coverage.CoveredPairCount, "one subject, one asked property, whatever else arrived.");
        Assert.AreEqual(0, coverage.PresentPairCount, "the unasked row is not a pair...");
        Assert.AreEqual(1, coverage.DerivedAbsences.Count, "...and the asked one is still absent.");
        Assert.AreEqual(2, coverage.RetainedRows.Count, "both non-admitted rows are kept by name.");
        Assert.ThrowsExactly<ArgumentException>(
            () => coverage.ValuesFor(requests[0], Unasked),
            "and nothing can be read out of the matrix about it.");
    }

    /// <summary>A delivered row about a subject this batch never asked about is refused.</summary>
    [TestMethod]
    public void ADeliveredRowNamingAnUnrequestedSubjectIsRefused()
    {
        var requests = Subjects(1);
        var assignment = Assignment(requests);

        Complete(assignment, out var refusal, out _, TypeRow(Request(97)));

        Assert.AreEqual(
            LuxembourgOpinionRequestCoverageRefusal.DeliveredRequestNotRequested, refusal);
    }

    /// <summary>
    /// Nothing can be read out of the matrix about a question this batch never asked.
    /// </summary>
    [TestMethod]
    public void TheMatrixRefusesToAnswerForSubjectsAndPropertiesOutsideIt()
    {
        var requests = Subjects(1);
        var coverage = Complete(
            Assignment(requests), out var refusal, out var detail, TypeRow(requests[0]));

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
    /// The kind markers this file compares against are the ones the query itself binds.
    /// </summary>
    /// <remarks>
    /// The key check compares a term's kind against a marker string, and the query's own BIND is
    /// what produces that marker. Pinned against the rendered template rather than against another
    /// constant, because two constants agreeing with each other prove nothing about the text that
    /// has to produce the value. The literal marker has no constant to alias, so this is the only
    /// thing holding it.
    /// </remarks>
    [TestMethod]
    public void TheKindMarkersAreTheOnesTheRenderedQueryBinds()
    {
        var plan = LuxembourgOpinionRequestGraphDiscoveryPlan.Create();
        const string BlankNode =
            LuxembourgOpinionRequestInventoryDiscoveryPlan.UnsupportedBlankNodeKind;

        StringAssert.Contains(
            plan.PageTemplate,
            $"IF(isIRI(?value), \"{IriKind}\", IF(isLiteral(?value), \"literal\", \"{BlankNode}\"))",
            "a row read under a marker the query never binds describes a delivery that never happened.");
        StringAssert.Contains(
            plan.PageTemplate,
            $"IF(isIRI(?request), \"{IriKind}\", \"{BlankNode}\")",
            "and the subject's marker is bound the same way.");
    }

    private static LuxembourgOpinionRequestCoverage? Complete(
        LuxembourgOpinionRequestBatchAssignment assignment,
        out LuxembourgOpinionRequestCoverageRefusal refusal,
        out string? detail,
        params (string Subject, string Predicate, string? Value, bool ValueIsIri)[] rows)
    {
        var (proof, delivered) = AbsenceFixtures.OpinionRequestGraphRows(
            assignment.PartitionKey, rows);
        return LuxembourgOpinionRequestCoverage.TryComplete(
            proof, delivered, assignment, out refusal, out detail);
    }

    private static (string Subject, string Predicate, string? Value, bool ValueIsIri) Value(
        string request,
        string value) => (request, ReferralDate, value, false);

    private static (string Subject, string Predicate, string? Value, bool ValueIsIri) TypeRow(
        string request) => (request, RdfType, RequestClass, true);

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
