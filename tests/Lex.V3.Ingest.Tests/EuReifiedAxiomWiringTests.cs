using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-05g/A3: family A reaches the production adapter, or it reaches nothing.
/// </summary>
/// <remarks>
/// <para>
/// A1 asked the publisher for the reified date axioms and A2 taught this codebase to read them, but
/// between those two slices the decode was unreachable from any real run: the adapter's own dispatch
/// read P, X, W and M and never opened family A's rows. A decoder nothing calls is exactly the
/// defect A1 existed to remove, one layer up, so these guards are about REACHABILITY rather than
/// about decoding, which <see cref="EuReifiedAxiomDecodeTests"/> already covers term by term.
/// </para>
/// <para>
/// Each case drives the real <see cref="EuQueryExecutionAdapter"/> over the fixture's scripted
/// transport and varies only family A's own script. Nothing here calls a publisher endpoint.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuReifiedAxiomWiringTests
{
    private const string Cdm = "http://publications.europa.eu/ontology/cdm#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string EntryIntoForce = Cdm + "resource_legal_date_entry-into-force";

    /// <summary>The skolemised shape family A measured, suffixed so it names a fixture.</summary>
    private const string AxiomIri = "http://publications.europa.eu/.well-known/genid/a3wiring/1";

    /// <summary>
    /// Renders family A's rows for one axiom in the order the page's own keyset delivers them.
    /// </summary>
    /// <remarks>
    /// The cursor is <c>key_1..key_7</c> over parent, axiom, predicate, value, kind, datatype and
    /// language, so with one parent and one axiom the page is ordered by predicate IRI ordinally --
    /// which puts <c>rdf:type</c> under <c>w3.org/1999</c> ahead of every <c>owl#</c> term and the
    /// <c>publications.europa.eu</c> annotations ahead of both. Delivering them in the order a
    /// reader finds natural instead makes the family fail to prove, which is a fixture bug rather
    /// than a finding, so the ordering is derived here rather than hand-written per case.
    /// </remarks>
    private static string[] AxiomRows(
        string parentIri,
        params (string Predicate, string Value, bool IsIri, string Datatype)[] terms) =>
        AxiomRows(parentIri, AxiomIri, terms);

    private static string[] AxiomRows(
        string parentIri,
        string axiomIri,
        params (string Predicate, string Value, bool IsIri, string Datatype)[] terms) =>
        [.. terms
            .OrderBy(static term => term.Predicate, StringComparer.Ordinal)
            .Select(term => EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
                parentIri, axiomIri, term.Predicate, term.Value, term.IsIri, term.Datatype))];

    /// <summary>A well-formed reified entry-into-force axiom on <paramref name="parentIri"/>.</summary>
    private static (string Predicate, string Value, bool IsIri, string Datatype)[] WellFormedTerms(
        string parentIri) =>
    [
        (Owl + "annotatedSource", parentIri, true, ""),
        (Owl + "annotatedProperty", EntryIntoForce, true, ""),
        (RdfType, Owl + "Axiom", true, ""),
        (Owl + "annotatedTarget", "2003-08-02", false, XsdDate),
    ];

    /// <summary>
    /// The production path decodes a delivered axiom all the way onto the run's own result.
    /// </summary>
    [TestMethod]
    public async Task ADeliveredAxiomReachesTheRunsOwnDecodedDateAxioms()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            rootIri => EuAcquisitionTestFixture.AxiomScriptFrom(
                AxiomRows(rootIri, WellFormedTerms(rootIri))));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.HasCount(1, result.DateAxioms);

        var binding = result.DateAxioms[0];
        Assert.AreEqual(AxiomIri, binding.Axiom.RemoteAxiomId);
        Assert.AreEqual("2003-08-02", binding.Fact.Date.RawLexicalValue);
        Assert.AreEqual(DatePrecision.YearMonthDay, binding.Fact.Date.Precision);
        Assert.AreEqual(EuReifiedAxiomDecode.ParsedByAuthority, binding.ParsedByAuthority);
        Assert.IsNull(binding.RawQualifierCode, "this fixture sends no type_of_date carrier.");
    }

    /// <summary>
    /// A binding names the custody coordinate its own rows were read from: family A's proof, and
    /// not another family's that happened to be at hand.
    /// </summary>
    /// <remarks>
    /// This exists because the comment claiming it was decoration. Substituting family P's
    /// interpretation-profile reference -- the exact wrong-family error the adapter's own remark
    /// says it prevents -- left the entire ingest suite green, so nothing in this project actually
    /// held the production path to it. <c>SourceObservationId</c> is a provenance claim; an
    /// unguarded one is a claim this run cannot support.
    /// </remarks>
    [TestMethod]
    public async Task ABindingNamesFamilyAsOwnProofAndNotAnotherFamilysThatWasToHand()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            rootIri => EuAcquisitionTestFixture.AxiomScriptFrom(
                AxiomRows(rootIri, WellFormedTerms(rootIri))));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.HasCount(1, result.DateAxioms);

        // Every object-facts family shares one FamilyKey and is told apart only by its own proof,
        // so family A is identified here by what it delivered: this fixture's four axiom rows,
        // against P's thirteen, X's one, W's one, M's six and the census family's none.
        var axiomFamily = result.FamilyOutcomes.Single(
            static outcome => outcome.DeliveredRowCount == 4);
        var otherFamilies = result.FamilyOutcomes
            .Where(outcome => !ReferenceEquals(outcome, axiomFamily))
            .Select(static outcome => outcome.Proof?.InterpretationProfileRef.ResourceId)
            .Where(static resourceId => resourceId is not null)
            .ToArray();

        Assert.IsNotNull(axiomFamily.Proof);
        Assert.AreEqual(
            axiomFamily.Proof!.InterpretationProfileRef.ResourceId,
            result.DateAxioms[0].Fact.SourceObservationId,
            "a binding must name family A's own interpretation-profile coordinate.");

        // The executor mints a distinct reference per family, so naming any other family's proof is
        // detectable rather than merely wrong in principle.
        CollectionAssert.DoesNotContain(
            otherFamilies,
            result.DateAxioms[0].Fact.SourceObservationId,
            "no other family's coordinate may stand in for family A's.");
    }

    /// <summary>
    /// Two delivered family-A batches, two distinct proofs: each binding names the coordinate its
    /// own batch was read from, not the first batch's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a hypothetical shape. The executor mints a distinct interpretation-profile reference per
    /// batch, and the accepted 82-seed population exceeds the fixed 50-object batch capacity before
    /// any consolidated state is added, so two or more family-A batches is ordinary production
    /// traffic. Reusing the first batch's reference makes every later binding carry a provenance
    /// claim the run cannot support, and it left the whole ingest suite green.
    /// </para>
    /// <para>
    /// Driven through the extracted batch seam rather than a full run: reaching two batches from
    /// the transport needs an observed-object set above the capacity, and building that fixture was
    /// hiding the batch-to-proof association rather than proving it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EachDeliveredBatchStampsItsOwnBindingsWithItsOwnProof()
    {
        const string ParentOne = "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";
        const string ParentTwo = "http://publications.europa.eu/resource/cellar/bbbbbbbb-0000-0000-0000-00000000000b";
        const string AxiomOne = "http://publications.europa.eu/.well-known/genid/batch-one/1";
        const string AxiomTwo = "http://publications.europa.eu/.well-known/genid/batch-two/1";

        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ReifiedAxiomFacts);
        var proofOne = new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-00000000aaa1", new string('a', 64));
        var proofTwo = new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-00000000bbb2", new string('b', 64));

        var batches = new (IReadOnlyList<RepeatedEnumerationRow>, RepeatedEnumerationInterpretationProfile, SourceArtifactRef)[]
        {
            (BatchRows(ParentOne, AxiomOne), profile, proofOne),
            (BatchRows(ParentTwo, AxiomTwo), profile, proofTwo),
        };

        var decoded = EuQueryExecutionAdapter.DecodeAxiomBatches(batches, out var refusal, out _);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded);

        var byAxiom = decoded.ToDictionary(
            static binding => binding.Axiom.RemoteAxiomId,
            static binding => binding.Fact.SourceObservationId,
            StringComparer.Ordinal);

        Assert.AreEqual(
            proofOne.ResourceId, byAxiom[AxiomOne],
            "the first batch's binding must name the first batch's coordinate.");
        Assert.AreEqual(
            proofTwo.ResourceId, byAxiom[AxiomTwo],
            "the second batch's binding must name its OWN coordinate, not the first batch's.");
        Assert.AreNotEqual(
            byAxiom[AxiomOne], byAxiom[AxiomTwo],
            "two batches with distinct proofs cannot produce one shared provenance.");
    }

    /// <summary>
    /// One well-formed axiom's delivered rows, in the shape the executor hands the adapter.
    /// </summary>
    private static IReadOnlyList<RepeatedEnumerationRow> BatchRows(string parentIri, string axiomIri) =>
        [.. WellFormedTerms(parentIri)
            .OrderBy(static term => term.Predicate, StringComparer.Ordinal)
            .Select(term => DeliveredRow(parentIri, axiomIri, term))];

    private static RepeatedEnumerationRow DeliveredRow(
        string parentIri, string axiomIri, (string Predicate, string Value, bool IsIri, string Datatype) term)
    {
        var value = term.IsIri
            ? RepeatedEnumerationRdfTerm.Iri(term.Value)
            : RepeatedEnumerationRdfTerm.Literal(
                term.Value, term.Datatype.Length == 0 ? null : term.Datatype, null);
        var kind = term.IsIri ? "iri" : "literal";
        var datatype = term.IsIri ? "" : term.Datatype;
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Iri(axiomIri),
            RepeatedEnumerationRdfTerm.Iri(term.Predicate),
            value,
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal(parentIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(axiomIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(term.Predicate, null, null),
            RepeatedEnumerationRdfTerm.Literal(term.Value, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[7..]), Array.AsReadOnly(terms[7..]));
    }

    /// <summary>
    /// The family's typed absence stays an absence on the production path: no bindings, no refusal.
    /// </summary>
    /// <remarks>
    /// Decision 64's shape. An empty <c>DateAxioms</c> is admissible here precisely because family A
    /// delivered its own bounded enumeration and said, in a typed row, that this work reifies
    /// nothing. That is a delivered fact rather than a family this run failed to ask.
    /// </remarks>
    [TestMethod]
    public async Task TheTypedAbsenceRowLeavesTheRunWithNoAxiomsAndNoRefusal()
    {
        var result = await EuAxiomWiringHarness.RunAsync(
            rootIri => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(rootIri));

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsEmpty(result.DateAxioms);
        Assert.AreEqual(EuQueryExecutionCompletion.AllFamiliesProven, result.Completion);
    }

    /// <summary>
    /// An axiom the accepted surface cannot read ends the run with a typed refusal naming family A,
    /// rather than being skipped so the run reports a smaller, cleaner axiom set than was sent.
    /// </summary>
    [TestMethod]
    public async Task AnUndecodableAxiomRefusesTheRunRatherThanBeingDroppedFromIt()
    {
        var result = await EuAxiomWiringHarness.RunAsync(rootIri =>
            EuAcquisitionTestFixture.AxiomScriptFrom(AxiomRows(
                rootIri,
                // Everything well formed except the declared type, which family A deliberately does
                // not require at the query so that a node missing it is retained and judged here.
                (Owl + "annotatedSource", rootIri, true, ""),
                (Owl + "annotatedProperty", EntryIntoForce, true, ""),
                (Owl + "annotatedTarget", "2003-08-02", false, XsdDate))));

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ReifiedAxiomDecodeRefused, result.Refusal!.Code);
        StringAssert.Contains(
            result.Refusal.Detail,
            nameof(EuReifiedAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom),
            "the run's refusal must name family A's own typed reason, not a generic failure.");
        Assert.IsEmpty(result.DateAxioms);
    }

    /// <summary>
    /// A malformed qualifier carrier reaches the same typed refusal, so the braced parse this slice
    /// exists around is genuinely on the production path and not only in the decode's own tests.
    /// </summary>
    [TestMethod]
    public async Task AMalformedQualifierCarrierRefusesOnTheProductionPath()
    {
        var result = await EuAxiomWiringHarness.RunAsync(rootIri =>
            EuAcquisitionTestFixture.AxiomScriptFrom(AxiomRows(
                rootIri,
                [
                    .. WellFormedTerms(rootIri),
                    // The publisher's shape is {CODE|IRI}; a bare code is not it.
                    ("http://publications.europa.eu/ontology/annotation#type_of_date",
                        "EV", false, XsdString),
                ])));

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ReifiedAxiomDecodeRefused, result.Refusal!.Code);
        StringAssert.Contains(
            result.Refusal.Detail, nameof(EuReifiedAxiomDecodeRefusal.QualifierTermMalformed));
    }

    /// <summary>
    /// Two seeds, one batch, one axiom each: each seed pass must take only its own work's axiom.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard I first claimed the harness could not build and deferred. The reviewer
    /// built it and showed the narrowing is load-bearing, so it is built here: without narrowing
    /// each seed pass decodes BOTH axioms and the run returns four bindings for two publisher
    /// facts, duplicating each work's axiom under the other. Nothing inside a binding is
    /// misattributed -- each still names its own annotatedSource -- which is why only the pairing
    /// below catches it.
    /// </para>
    /// <para>
    /// THE TWO AXIOMS CARRY DISTINCT IDENTIFIERS ON PURPOSE. An earlier version of this guard gave
    /// both works the same axiom IRI. It still failed when the narrowing was removed, but for the
    /// wrong reason: the decode grouped both works' rows under one axiom and refused with
    /// <see cref="EuReifiedAxiomDecodeRefusal.ModelledPredicateDeliveredMoreThanOnce"/> on
    /// <c>annotatedSource</c>, so the cross-seed duplication this test names was never reached. A
    /// guard that fails for an unrelated reason is not evidence for the reason it claims.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task EachSeedTakesOnlyItsOwnAxiomWhenTwoWorksShareOneBatch()
    {
        const string AxiomOne = "http://publications.europa.eu/.well-known/genid/a3wiring/seed-one";
        const string AxiomTwo = "http://publications.europa.eu/.well-known/genid/a3wiring/seed-two";

        // The roots the script was actually built from, so the expected pairing is the one the
        // publisher was made to assert rather than one restated from the result.
        string? suppliedOne = null;
        string? suppliedTwo = null;

        var result = await EuAxiomWiringHarness.RunTwoSeedAsync((rootOne, rootTwo) =>
        {
            suppliedOne = rootOne;
            suppliedTwo = rootTwo;
            return EuAcquisitionTestFixture.AxiomScriptFrom(
                [
                    .. AxiomRows(rootOne, AxiomOne, WellFormedTerms(rootOne)),
                    .. AxiomRows(rootTwo, AxiomTwo, WellFormedTerms(rootTwo)),
                ]);
        });

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.HasCount(
            2, result.DateAxioms,
            "two publisher axioms over two works must decode to exactly two bindings.");

        // The exact pairs, not two independent facts about them. Asserting only that the works are
        // distinct and that the axiom identifiers are the expected SET is satisfied by the swapped
        // pairing, in which each work carries precisely the axiom the other one asserted.
        var pairs = result.DateAxioms
            .Select(static binding => (
                Work: binding.WorkIdentity.Identifiers[0].RawValue,
                Axiom: binding.Axiom.RemoteAxiomId))
            .OrderBy(static pair => pair.Work, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { (suppliedOne!, AxiomOne), (suppliedTwo!, AxiomTwo) },
            pairs,
            "each work must carry exactly the axiom it asserted, and only that one.");
    }

    // Why the narrowing needs the two-seed case above and cannot be reached by the single-seed
    // ones: the executor already refuses any row outside the requested batch (family A's
    // batch-membership key is key_1) before the adapter sees it, so with one requested seed no
    // foreign parent survives to be narrowed away. The narrowing bites only on a batch carrying
    // MORE THAN ONE requested seed, which is what EachSeedTakesOnlyItsOwnAxiomWhenTwoWorksShareOneBatch
    // delivers.

    /// <summary>
    /// Family A is required, so a run that never proves it refuses instead of quietly reporting no
    /// axioms at all.
    /// </summary>
    /// <remarks>
    /// This is the difference between "the publisher reified nothing" and "we never asked", and
    /// Decision 64 admits an empty set only for the first. Without this the wiring above would let
    /// an unproven family read exactly like a work with no axioms.
    /// </remarks>
    [TestMethod]
    public async Task ARunThatNeverProvesFamilyARefusesRatherThanReportingNoAxioms()
    {
        // Family A is asked and answers, but its own count disagrees with what it delivered, so the
        // enumeration does not prove. That is the shape a family fails in; omitting the script
        // entirely would only prove the fixture throws.
        var result = await EuAxiomWiringHarness.RunAsync(rootIri =>
            EuAcquisitionTestFixture.ScriptFor(
                "A",
                selected: 5,
                [EuAcquisitionTestFixture.ReifiedAxiomFactsUnboundRow(rootIri)],
                EuAcquisitionTestFixture.ReifiedAxiomFactsProjection));

        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(EuQueryExecutionRefusal.ObjectFactsFamilyNotProven, result.Refusal!.Code);
        Assert.IsEmpty(result.DateAxioms);
    }
}
