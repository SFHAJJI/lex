using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What a draft's referral date resolves to over two independently proven families.
/// </summary>
/// <remarks>
/// <para>
/// The five states are the point. "Present" and "absent" cannot carry this question: a draft reaches
/// several opinion events and only one of them is an <c>OpinionRequest</c>, so most edges are
/// neither an answer nor a missing answer. Each case below lands on a different state, and the two
/// gap states are kept apart deliberately — a draft that reached nothing and one that reached
/// something unproven are different facts about the publisher.
/// </para>
/// <para>
/// EVERY INPUT TO <c>Resolve</c> IS DERIVED, and one case pins that the composition takes only two
/// parameters so there is nowhere for a seventh boolean to arrive from.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgReferralDateCompositionTests
{
    private const string HasOpinion = LuxembourgOpinionLinkOnlyVocabulary.HasOpinionPredicateIri;

    private const string ReferralDate =
        LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;

    private const string RdfType = LuxembourgOpinionRequestGraphDiscoveryPlan.RdfTypePredicateIri;

    private const string RequestClass =
        LuxembourgOpinionRequestGraphDiscoveryPlan.OpinionRequestClassIri;

    private const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

    /// <summary>A typed target holding a date resolves to a fact, with every value kept.</summary>
    [TestMethod]
    public void ATypedTargetHoldingADateResolvesToAFact()
    {
        var draft = Draft(1);
        var target = Target(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, HasOpinion, target, true)),
            RequestCover([target], typed: true, date: "2004-03-11"));

        var step = steps.Single();
        Assert.AreEqual(LuxembourgReferralDateState.Fact, step.State);
        Assert.AreEqual(draft, step.DraftIri);
        Assert.AreEqual(target, step.TargetIri);
        CollectionAssert.AreEqual(new[] { "2004-03-11" }, step.ReferralDateValues.ToArray());
    }

    /// <summary>
    /// A typed target holding no date is the one silence this family may read.
    /// </summary>
    /// <remarks>
    /// And only because the delivery that was silent COULD have carried it: a complete
    /// broad-predicate enumeration over a subject the publisher itself typed. Nothing weaker.
    /// </remarks>
    [TestMethod]
    public void ATypedTargetHoldingNoDateIsADerivedAbsence()
    {
        var draft = Draft(1);
        var target = Target(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, HasOpinion, target, true)),
            RequestCover([target], typed: true, date: null));

        var step = steps.Single();
        Assert.AreEqual(LuxembourgReferralDateState.DerivedAbsence, step.State);
        Assert.AreEqual(target, step.TargetIri, "an absence names the request it is about.");
        Assert.AreEqual(0, step.ReferralDateValues.Count);
    }

    /// <summary>
    /// A draft that reached no opinion request at all was never asked the question.
    /// </summary>
    /// <remarks>
    /// Not an absence. Reporting it as "no referral date" would be the false absence S2-A03 forbids,
    /// and 1 of the 650 drafts in the retained sample is in exactly this position.
    /// </remarks>
    [TestMethod]
    public void ADraftThatReachedNothingIsADraftSideGap()
    {
        var draft = Draft(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                "http://example.invalid/status", true)),
            RequestCover([Target(1)], typed: true, date: "2004-03-11"));

        var step = steps.Single();
        Assert.AreEqual(LuxembourgReferralDateState.DraftSideGap, step.State);
        Assert.IsNull(step.TargetIri, "nothing was reached, so there is nothing to name.");
    }

    /// <summary>
    /// A target outside the proven request class is its own state, not a draft-side gap.
    /// </summary>
    /// <remarks>
    /// <c>hasOpinion</c> reaches other opinion classes — the retained sample shows <c>avce</c>,
    /// <c>avis</c> and <c>disp</c> targets alongside the single <c>sace</c> — so the draft DID reach
    /// something, and saying otherwise would hide which edge was unresolved.
    /// </remarks>
    [TestMethod]
    public void ATargetOutsideTheProvenInventoryIsATargetRoleGap()
    {
        var draft = Draft(1);
        var stranger = "http://data.legilux.public.lu/eli/dl/pl/2000/0001/evenement/avis/1";

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, HasOpinion, stranger, true)),
            RequestCover([Target(1)], typed: true, date: "2004-03-11"));

        var step = steps.Single();
        Assert.AreEqual(LuxembourgReferralDateState.TargetRoleGap, step.State);
        Assert.AreEqual(
            stranger, step.TargetIri, "the edge stays nameable, which is what separates this state.");

        // THE REFUSAL IS THE DISCRIMINATOR, NOT THE STATE, and asserting only the state is how this
        // case passed while the membership check was disabled. Resolve returns TargetRoleGap for
        // BOTH a target outside the class and a target inside it whose evidence failed - the first
        // carries Refusal.None because it is a fact about the publisher's graph, the second names
        // what was missing. A test blind to that cannot tell an answer from a failure.
        Assert.AreEqual(
            LuxembourgReferralEvidenceRefusal.None, step.Refusal,
            "a target outside the proven class is a fact, not an evidence failure.");
    }

    /// <summary>
    /// A referral date asserted directly of the draft is retained drift, never a fact.
    /// </summary>
    /// <remarks>
    /// <c>referralDate</c> is declared on <c>OpinionRequest</c>; a triple asserting it of an
    /// <c>InitialDraft</c> says that subject holds a class role nothing proved. The draft graph
    /// retains it rather than admitting it, and reading it here as a date would be this family
    /// widening its own authority through the back door.
    /// </remarks>
    [TestMethod]
    public void ADirectReferralDateTripleOnTheDraftIsDrift()
    {
        var draft = Draft(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction(
                (draft, ReferralDate, "2004-03-11", false),
                (draft, HasOpinion, Target(1), true)),
            RequestCover([Target(1)], typed: true, date: "2004-03-11"));

        var step = steps.Single();
        Assert.AreEqual(
            LuxembourgReferralDateState.Drift, step.State,
            "drift is reported for the draft whatever its edges say.");
        Assert.IsNull(step.TargetIri, "nothing was traversed to reach a drifted triple.");
    }

    /// <summary>
    /// A target the publisher never typed refuses, and the refusal names the missing evidence.
    /// </summary>
    /// <remarks>
    /// The class filter in the request query is not type evidence. Without the delivered row this is
    /// an evidence failure rather than an absence, and the two must not collapse.
    /// </remarks>
    [TestMethod]
    public void AnUntypedTargetRefusesOnTheMissingTypeRow()
    {
        var draft = Draft(1);
        var target = Target(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, HasOpinion, target, true)),
            RequestCover([target], typed: false, date: null));

        var step = steps.Single();
        Assert.AreEqual(
            LuxembourgReferralEvidenceRefusal.DeliveredTypeRowMissing, step.Refusal);
        Assert.AreEqual(0, step.ReferralDateValues.Count);
    }

    /// <summary>
    /// A target delivered as something other than a bare IRI is refused as such.
    /// </summary>
    [TestMethod]
    public void ATargetThatIsNotABareIriIsRefused()
    {
        var draft = Draft(1);

        var steps = LuxembourgReferralDateComposition.Over(
            DraftProduction((draft, HasOpinion, "not-an-iri", false)),
            RequestCover([Target(1)], typed: true, date: "2004-03-11"));

        Assert.AreEqual(
            LuxembourgReferralEvidenceRefusal.TargetNotABareIri, steps.Single().Refusal);
    }

    /// <summary>A refused draft production composes nothing.</summary>
    /// <remarks>
    /// It carries no citation, so nothing it holds is bound to a proven delivery - and an edge that
    /// is not bound to one is a value someone passed in.
    /// </remarks>
    [TestMethod]
    public void ARefusedDraftProductionComposesNothing()
    {
        var refused = LuxembourgDraftGraphProductionResult.Refused(
            LuxembourgDraftGraphProductionRefusal.EnumerationRefused, "refused", 0);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgReferralDateComposition.Over(
                refused, RequestCover([Target(1)], typed: true, date: "2004-03-11")));
    }

    /// <summary>
    /// The composition takes a proven production and a proven cover, and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>Resolve</c> takes six booleans and a value list, every one of which is a conclusion if a
    /// caller can state it. A third parameter here is where the first of them would arrive, so the
    /// surface is pinned rather than the intention commented - the same rule #556 established for
    /// the coverage door.
    /// </remarks>
    [TestMethod]
    public void TheCompositionTakesOnlyTwoProvenInputs()
    {
        var parameters = typeof(LuxembourgReferralDateComposition)
            .GetMethod(nameof(LuxembourgReferralDateComposition.Over))!
            .GetParameters();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(LuxembourgDraftGraphProductionResult),
                typeof(LuxembourgOpinionRequestBatchCover),
            },
            parameters.Select(static value => value.ParameterType).ToArray(),
            "a third parameter is where a caller-stated conclusion would arrive.");
    }

    /// <summary>A delivered draft-graph production over the given retained edges.</summary>
    /// <remarks>
    /// The edges arrive as RETAINED rows because no family admits <c>hasOpinion</c>: the draft graph
    /// asks for every predicate the publisher holds and admits five, so the edge is delivered,
    /// key-verified and kept by name.
    /// </remarks>
    private static LuxembourgDraftGraphProductionResult DraftProduction(
        params (string Draft, string Predicate, string Value, bool Iri)[] rows)
    {
        var drafts = rows.Select(static row => row.Draft).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var assignment = LuxembourgDraftGraphBatchFactory.AssignBatches(DraftInventory(drafts))[0];

        var ordered = rows
            .OrderBy(static row => row.Draft, StringComparer.Ordinal)
            .ThenBy(static row => IriKind, StringComparer.Ordinal)
            .ThenBy(static row => row.Predicate, StringComparer.Ordinal)
            .ThenBy(
                static row => LuxembourgPublisherCursorCodec.ComputeKey(row.Value),
                StringComparer.Ordinal)
            .ToArray();

        // The canonical keys come from a real proven delivery of this partition; the terms are the
        // rows under test. The producer checks the projected key columns in the terms, and the
        // citation door checks the canonical keys against the proof - two different claims.
        var tokens = Enumerable.Range(0, ordered.Length).Select(i => $"e{i:D4}").ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(assignment.PartitionKey, tokens);

        var bound = ordered
            .Select((row, index) => new RepeatedEnumerationRow(
                DraftTerms(row.Draft, row.Predicate, row.Value, row.Iri), keys[index], keys[index]))
            .ToArray();

        var result = LuxembourgDraftGraphProducer.DecodeRows(
            bound,
            LuxembourgDraftGraphDiscoveryPlan.Create().CreateDeliveryProfile(),
            proof,
            assignment,
            assignment.PartitionKey,
            "2026-09-11T12:00:00.0000000Z");

        Assert.IsNotNull(result.Coverage, $"{result.Refusal}: {result.Detail}");
        return result;
    }

    private static IReadOnlyList<RepeatedEnumerationRdfTerm> DraftTerms(
        string draft, string predicate, string value, bool iri)
    {
        var valueKind = iri ? IriKind : "literal";
        return
        [
            RepeatedEnumerationRdfTerm.Iri(draft),
            RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
            RepeatedEnumerationRdfTerm.Iri(predicate),
            iri
                ? RepeatedEnumerationRdfTerm.Iri(value)
                : RepeatedEnumerationRdfTerm.Literal(value, null, null),
            RepeatedEnumerationRdfTerm.Literal(valueKind, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", "http://www.w3.org/2001/XMLSchema#integer", null),
            RepeatedEnumerationRdfTerm.Literal(draft, null, null),
            RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicate, null, null),
            RepeatedEnumerationRdfTerm.Literal(
                LuxembourgPublisherCursorCodec.ComputeKey(value), null, null),
            RepeatedEnumerationRdfTerm.Literal(valueKind, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        ];
    }

    private static LuxembourgInitialDraftInventoryResult DraftInventory(IReadOnlyList<string> drafts)
    {
        var ordered = drafts.Order(StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgInitialDraftInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);
        var rows = ordered
            .Select((draft, index) => new RepeatedEnumerationRow(
                [
                    RepeatedEnumerationRdfTerm.Iri(draft),
                    RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
                    RepeatedEnumerationRdfTerm.Literal(
                        "1", "http://www.w3.org/2001/XMLSchema#integer", null),
                    keys[index][0],
                    keys[index][1],
                ],
                keys[index],
                keys[index]))
            .ToArray();

        var result = LuxembourgInitialDraftInventoryProducer.DecodeRows(
            rows,
            LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile(),
            proof,
            "2026-09-11T12:00:00.0000000Z");
        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        return result;
    }

    /// <summary>A proven request cover over one batch of the given targets.</summary>
    private static LuxembourgOpinionRequestBatchCover RequestCover(
        IReadOnlyList<string> targets,
        bool typed,
        string? date)
    {
        var population = targets.Order(StringComparer.Ordinal).ToArray();
        var inventory = RequestInventory(population);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(population, inventory)[0];

        var specs = new List<(string Subject, string Predicate, string? Value, bool ValueIsIri)>();
        foreach (var target in population)
        {
            if (typed)
            {
                specs.Add((target, RdfType, RequestClass, true));
            }

            if (date is not null)
            {
                specs.Add((target, ReferralDate, date, false));
            }
        }

        var (proof, rows) = AbsenceFixtures.OpinionRequestGraphRows(assignment.PartitionKey, specs);
        var coverage = LuxembourgOpinionRequestCoverage.TryComplete(
            proof, rows, assignment, out var refusal, out var detail);
        Assert.IsNotNull(coverage, $"{refusal}: {detail}");

        var cover = LuxembourgOpinionRequestBatchCover.TryCreate(
            population, inventory, [coverage], out var coverRefusal, out var coverDetail);
        Assert.IsNotNull(cover, $"{coverRefusal}: {coverDetail}");
        return cover;
    }

    private static LuxembourgOpinionRequestInventoryCitation RequestInventory(
        IReadOnlyList<string> subjects)
    {
        var ordered = subjects.Order(StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered);
    }

    private static string Draft(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}";

    private static string Target(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D4}/evenement/sace/1";
}
