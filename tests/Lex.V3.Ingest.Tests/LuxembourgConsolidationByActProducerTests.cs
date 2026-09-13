using System.Text.Json;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #419 slice 2, the per-act decoder: the consolidations of ONE act, read from a proven delivery.
/// </summary>
/// <remarks>
/// The properties tested here are the two the design review's conditions 4 and 5 rest on and the
/// plan (slice 1) could not carry alone: every delivered row must name the selected act in its
/// projected <c>act</c> column before it becomes a consolidation, and a delivery whose partition is
/// not the act's own key cannot be proven at all. Around those, the same row-shape discipline the
/// inventory decoder beside this one enforces - a subject that is really a subject, a marker that
/// agrees, a key that keys its own terms, a positive grouped count - plus the two facts that make a
/// count honest later: an act with no consolidations is an ANSWER, and a repeated one is refused.
/// </remarks>
[TestClass]
public sealed class LuxembourgConsolidationByActProducerTests
{
    private const string Act = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
    private const string OtherAct = "http://data.legilux.public.lu/eli/etat/leg/rgd/2019/07/12/a512/jo";
    private const string ConsA =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/consolide/20200101";
    private const string ConsB =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/consolide/20210701";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        LuxembourgConsolidationByActDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) => RepeatedEnumerationRdfTerm.Iri(value);
    private static RepeatedEnumerationRdfTerm Blank(string label) => RepeatedEnumerationRdfTerm.BlankNode(label);
    private static RepeatedEnumerationRdfTerm Literal(string value, string? datatype = null, string? language = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, language);

    private static string MarkerFor(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => LuxembourgConsolidationByActDiscoveryPlan.IriKind,
        _ => LuxembourgConsolidationByActDiscoveryPlan.UnsupportedBlankNodeKind,
    };

    /// <summary>
    /// One delivered row, every term, marker, act and key chosen independently.
    /// </summary>
    /// <remarks>
    /// Independence is the point, exactly as in the inventory decoder's fixture: a builder that
    /// derived the marker, the act and the keys from the subject could never express the deliveries
    /// this decoder exists to refuse, and every disagreement guard would be unreachable from its own
    /// fixture. The defaults are coherent, so an honest row takes no arguments.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm? consolidation = null,
        string? consolidationKind = null,
        RepeatedEnumerationRdfTerm? consolidationKindTerm = null,
        RepeatedEnumerationRdfTerm? act = null,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? key1 = null,
        string? key2 = null,
        RepeatedEnumerationRdfTerm? key1Term = null)
    {
        var subject = consolidation ?? Iri(ConsA);
        var marker = consolidationKind ?? MarkerFor(subject);
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            subject,
            consolidationKindTerm ?? Literal(marker),
            act ?? Iri(Act),
            multiplicity ?? Literal("1", XsdInteger),
            key1Term ?? Literal(key1 ?? subject.Value ?? string.Empty),
            Literal(key2 ?? marker),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static LuxembourgConsolidationByActResult Decode(params RepeatedEnumerationRow[] rows) =>
        DecodeWith(ProofOf(rows.Length), rows);

    /// <summary>A proof whose delivered-row count is exactly <paramref name="rowCount"/>.</summary>
    /// <remarks>
    /// The count is honest, zero included, because the frame consumer reads
    /// <see cref="AbsenceFamilyEnumerationProof.DeliveredRowCount"/> to admit a delivered-no-rows or
    /// delivered-rows disposition; a fixture that reported one row for a zero-row decode could not
    /// establish the zero-row handoff, which is one of the two things this repair closes.
    /// </remarks>
    private static AbsenceFamilyEnumerationProof ProofOf(int rowCount) =>
        AbsenceFixtures.Delivery("lu-consolidation-by-act-test", rowCount).Proof;

    private static LuxembourgConsolidationByActResult DecodeWith(
        AbsenceFamilyEnumerationProof proof, params RepeatedEnumerationRow[] rows) =>
        LuxembourgConsolidationByActProducer.DecodeRows(
            rows, Profile(), proof, Act, LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());

    // ---- the two bindings slice 2 exists to add --------------------------------------------------

    /// <summary>
    /// HOSTILE CASE (the design review's sixth): a delivered row naming another act refuses the whole
    /// delivery, by its own name, rather than being read or dropped.
    /// </summary>
    /// <remarks>
    /// The act is bound through <c>VALUES ?act</c> and projected, so a real row carries the act the
    /// publisher's triple joined against. A row whose <c>act</c> column is a different IRI is a
    /// delivery this run cannot read as its act's consolidations. It is refused rather than dropped:
    /// a dropped row would make an under-count read as a complete answer, which is the exact shape a
    /// never-consolidated claim must never rest on.
    /// </remarks>
    [TestMethod]
    public void ARowNamingAnotherActRefusesTheWholeDelivery()
    {
        var result = Decode(Row(), Row(consolidation: Iri(ConsB), act: Iri(OtherAct)));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNamesAnotherAct, result.Refusal);
        Assert.IsNull(result.Consolidations);
        StringAssert.Contains(result.Detail!, OtherAct);
    }

    /// <summary>An <c>act</c> column that is not an IRI is not this act either, and refuses.</summary>
    [TestMethod]
    public void ARowWhoseActColumnIsNotAnIriRefuses()
    {
        var result = Decode(Row(act: Literal(Act)));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNamesAnotherAct, result.Refusal);
    }

    // ---- the answer, and the honest empty answer -------------------------------------------------

    /// <summary>A delivered consolidation carries its own terms.</summary>
    [TestMethod]
    public void ADeliveredConsolidationCarriesItsOwnTerms()
    {
        var result = Decode(Row(consolidation: Iri(ConsA), multiplicity: Literal("2", XsdInteger)));

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.AreEqual(Act, result.Act);
        Assert.HasCount(1, result.Consolidations!);
        var one = result.Consolidations![0];
        Assert.AreEqual(ConsA, one.Value);
        Assert.AreEqual(LuxembourgConsolidationByActDiscoveryPlan.IriKind, one.Kind);
        Assert.AreEqual(2, one.Multiplicity);
        Assert.IsNotNull(result.Proof);
        Assert.IsGreaterThanOrEqualTo(1, result.Proof!.DeliveredRowCount);
    }

    /// <summary>
    /// A blank-node coordinated text is carried, not dropped: it still consolidates the act.
    /// </summary>
    /// <remarks>
    /// This is where this decoder deliberately differs from the InitialDraft inventory, which
    /// refuses a non-addressable subject because its whole output is an exactly-addressable list. A
    /// consolidation is a terminal fact, not something a later stage re-addresses, so a blank-node
    /// consolidation means the act IS consolidated and must be carried with its kind, never filtered
    /// into a false "never consolidated".
    /// </remarks>
    [TestMethod]
    public void ABlankNodeConsolidationIsCarriedNotDropped()
    {
        var result = Decode(Row(consolidation: Blank("b0")));

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(1, result.Consolidations!);
        Assert.AreEqual(
            LuxembourgConsolidationByActDiscoveryPlan.UnsupportedBlankNodeKind,
            result.Consolidations![0].Kind);
        Assert.AreEqual("b0", result.Consolidations[0].Value);
    }

    /// <summary>
    /// An act with no consolidations in this run is an ANSWER, not a refusal.
    /// </summary>
    /// <remarks>
    /// The empty list is the whole reason slice 1 removed the class restriction that returned zero
    /// rows for a consolidated act: a zero-row delivery has to mean "no subject asserted
    /// <c>consolidates</c> against this act, in this run", and nothing else. It is emphatically not
    /// the terminal never-consolidated-in-law claim, which the frame and a bounded live acceptance
    /// carry, and which this slice does not build.
    /// </remarks>
    [TestMethod]
    public void AnActWithNoConsolidationsInThisRunIsAnAnswer()
    {
        var result = Decode();

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsNotNull(result.Consolidations);
        Assert.IsEmpty(result.Consolidations!);
        Assert.IsNotNull(result.Proof);
        Assert.AreEqual(
            0, result.Proof!.DeliveredRowCount,
            "the carried proof reports zero rows, so the frame can admit a delivered-no-rows entry.");
    }

    /// <summary>
    /// The exact proof used to read the rows is carried onto the result by object identity, and a
    /// refusal carries none.
    /// </summary>
    /// <remarks>
    /// The finding this repair closes: the first head kept only the acquisition-run reference off the
    /// proof, which cannot reconstruct the family key, row count, digests or profile refs the merged
    /// <c>LuxembourgNeverConsolidatedEntry</c> requires. The proof is carried unchanged - the same
    /// object the decoder read from - so the frame consumes it without rerunning.
    /// </remarks>
    [TestMethod]
    public void TheExactProofSurvivesOntoADeliveredResultAndARefusalCarriesNone()
    {
        var proof = ProofOf(1);
        var delivered = DecodeWith(proof, Row());

        Assert.IsTrue(delivered.Delivered, $"{delivered.Refusal}: {delivered.Detail}");
        Assert.AreSame(
            proof, delivered.Proof,
            "the frame needs the exact proof by identity, not a reference derived from it.");
        Assert.AreEqual(proof.AcquisitionRunRef, delivered.CompletionEvidenceRef);

        var refused = DecodeWith(ProofOf(1), Row(act: Iri(OtherAct)));
        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNamesAnotherAct, refused.Refusal);
        Assert.IsNull(refused.Proof, "a refusal carries no proof.");
        Assert.IsNull(refused.CompletionEvidenceRef);
    }

    /// <summary>
    /// A delivered result's proof builds the frame's own never-consolidated entry, both ways, with no
    /// rerun - the whole reason the proof must survive this slice's public boundary.
    /// </summary>
    /// <remarks>
    /// This reaches into the merged <c>LuxembourgNeverConsolidatedEntry</c> (Contracts), the exact
    /// consumer the review named. A zero-row result admits <c>CitedEnumerationDeliveredNoRows</c>; a
    /// delivered-rows result admits <c>CitedEnumerationDeliveredRows</c>; each is refused if paired
    /// with the disposition its own proof contradicts, so the handoff is real rather than nominal.
    /// </remarks>
    [TestMethod]
    public void ADeliveredResultsProofBuildsTheFramesNeverConsolidatedEntry()
    {
        const string Loi = "http://data.legilux.public.lu/resource/authority/legal-type/LOI";
        var actClass = new LuxembourgActClassRef(Loi);

        var none = Decode();
        var noRows = new LuxembourgNeverConsolidatedEntry(
            Act, actClass,
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            none.Proof);
        Assert.AreSame(none.Proof, noRows.EnumerationCompletionProof);

        var some = DecodeWith(ProofOf(1), Row());
        var withRows = new LuxembourgNeverConsolidatedEntry(
            Act, actClass,
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredRows,
            some.Proof);
        Assert.AreSame(some.Proof, withRows.EnumerationCompletionProof);

        // AND THE PROOF STILL DECIDES: the delivered-rows proof cannot back a no-rows member.
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgNeverConsolidatedEntry(
            Act, actClass,
            LuxembourgNeverConsolidatedDisposition.CitedEnumerationDeliveredNoRows,
            some.Proof));
    }

    // ---- the row-shape discipline, mirrored from the inventory decoder ---------------------------

    [TestMethod]
    public void AConsolidationSubjectOfAKindRdfCannotPutInSubjectPositionIsRefused()
    {
        var result = Decode(Row(consolidation: Literal("not-a-subject")));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, result.Refusal);
    }

    [TestMethod]
    public void AMarkerDisagreeingWithItsSubjectOrCarryingAQualifierRefusesTheRow()
    {
        var wrongValue = Decode(Row(consolidationKindTerm:
            Literal(LuxembourgConsolidationByActDiscoveryPlan.UnsupportedBlankNodeKind)));
        var qualified = Decode(Row(consolidationKindTerm:
            Literal(LuxembourgConsolidationByActDiscoveryPlan.IriKind, language: "en")));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, wrongValue.Refusal);
        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, qualified.Refusal);
    }

    [TestMethod]
    public void AKeyThatDoesNotKeyItsOwnTermsRefusesTheRow()
    {
        var key1Wrong = Decode(Row(key1: "not-the-subject"));
        var key2Wrong = Decode(Row(key2: "not-the-kind"));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, key1Wrong.Refusal);
        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, key2Wrong.Refusal);
    }

    [TestMethod]
    public void TheGroupedCountMustBeAPositiveInteger()
    {
        var zero = Decode(Row(multiplicity: Literal("0", XsdInteger)));
        var untyped = Decode(Row(multiplicity: Literal("1")));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, zero.Refusal);
        Assert.AreEqual(LuxembourgConsolidationByActRefusal.RowNotAdmitted, untyped.Refusal);
    }

    [TestMethod]
    public void AConsolidationDeliveredTwiceIsRefused()
    {
        var result = Decode(Row(consolidation: Iri(ConsA)), Row(consolidation: Iri(ConsA)));

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.ConsolidationDeliveredTwice, result.Refusal);
    }

    // ---- the request record's own edge refusals --------------------------------------------------

    /// <summary>The request canonicalizes its act at construction, so an inadmissible one never runs.</summary>
    [TestMethod]
    public void TheRequestCanonicalizesItsActAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgConsolidationByActRunRequest(
            LuxembourgConsolidationByActDiscoveryPlan.Create(),
            "http://data.legilux.public.lu/eli/etat/leg/loi\\2017/03/14/a439/jo",
            NewUrn(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9401),
            LuxembourgAcquisitionTestFixture.TestWireBudget()));

        var admitted = new LuxembourgConsolidationByActRunRequest(
            LuxembourgConsolidationByActDiscoveryPlan.Create(),
            Act,
            NewUrn(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9401),
            LuxembourgAcquisitionTestFixture.TestWireBudget());
        Assert.AreEqual(Act, admitted.PublisherActIri, "the stored act is the one admitted spelling.");
    }

    [TestMethod]
    public void TheRequestRefusesANullBudget() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgConsolidationByActRunRequest(
            LuxembourgConsolidationByActDiscoveryPlan.Create(),
            Act,
            NewUrn(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9401),
            null!));

    // ---- the door: budget reserved before the session, and the whole family end to end ----------

    /// <summary>An exhausted budget opens no session, so not even robots reaches the publisher.</summary>
    /// <remarks>
    /// THE ASSERTION IS ON THE TRANSPORT, not on the refusal alone. A door that skipped its
    /// pre-session robots reservation would still return a refusal here - the pass loop would refuse
    /// the count once the session was open - but it would have sent robots first. <c>SendCount == 0</c>
    /// is the only observation that separates a door that stopped from one that fetched robots and
    /// then discovered the exhaustion, exactly as the EU procedure-event door's closure test pins it.
    /// </remarks>
    [TestMethod]
    public async Task ASpentBudgetOpensNoSessionOnTheConsolidationByActDoor()
    {
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler(
            static (_, _) => throw new AssertFailedException("nothing may reach the publisher."));
        var producer = new LuxembourgConsolidationByActProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);
        var budget = WireRequestBudget.OfWireRequests(2);
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.TryReserveAttempt());
        Assert.IsTrue(budget.Exhausted);

        var result = await producer.RunAsync(
            new LuxembourgConsolidationByActRunRequest(
                LuxembourgConsolidationByActDiscoveryPlan.Create(),
                Act,
                NewUrn(),
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9401),
                budget),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(LuxembourgConsolidationByActRefusal.EnumerationRefused, result.Refusal);
        Assert.AreEqual(0, result.ProductRequestCount);
        Assert.IsNull(result.Consolidations);
        Assert.AreEqual(
            0, handler.SendCount,
            "nothing may reach the publisher once the ceiling is spent - not even robots.");
    }

    /// <summary>
    /// The family runs end to end for one act and cites the run's own evidence.
    /// </summary>
    /// <remarks>
    /// Two passes, count then page, exactly as the publisher answers; the page delivers two IRI
    /// consolidations of the requested act, keyed by the coordinated text. The completion evidence
    /// the consolidations cite is the acquisition RUN, taken from the proof, not one request's HTTP
    /// evidence - the same binding the inventory family proves for itself.
    /// </remarks>
    [TestMethod]
    public async Task TheFamilyRunsEndToEndForOneActAndCitesTheRunsOwnEvidence()
    {
        var plan = LuxembourgConsolidationByActDiscoveryPlan.Create();
        var page = ConsolidationPageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(2),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgConsolidationByActProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            new LuxembourgConsolidationByActRunRequest(
                plan,
                Act,
                NewUrn(),
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9401),
                LuxembourgAcquisitionTestFixture.TestWireBudget()),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(2, result.Consolidations!);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
        CollectionAssert.AreEqual(
            new[] { ConsA, ConsB }.Order(StringComparer.Ordinal).ToArray(),
            result.Consolidations!.Select(static value => value.Value).ToArray());

        Assert.IsNotNull(result.Proof);
        Assert.AreEqual(
            2, result.Proof!.DeliveredRowCount,
            "the real run's proof is carried and reports the two delivered rows.");
        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            System.Text.Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the consolidations cite the acquisition RUN, not one request's HTTP evidence.");
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9401);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:6f2c8b41-9d05-4a73-bc18-2e947d0f5a36",
            "urn:uuid:8a3d5e29-1b74-4c60-9e82-5f0316bd47c9",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9401)).Request;
    }

    /// <summary>Two IRI consolidations of the act, in the publisher's wire shape, ordered by keyset.</summary>
    private static string ConsolidationPageJson(IReadOnlyList<string> projection)
    {
        static Dictionary<string, string> IriTerm(string value) =>
            new(StringComparer.Ordinal) { ["type"] = "uri", ["value"] = value };

        static Dictionary<string, string> LiteralTerm(string value, string? datatype = null)
        {
            var term = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["type"] = "literal",
                ["value"] = value,
            };
            if (datatype is not null)
            {
                term["datatype"] = datatype;
            }

            return term;
        }

        var kind = LuxembourgConsolidationByActDiscoveryPlan.IriKind;
        var bindings = new List<Dictionary<string, object>>();
        foreach (var consolidation in new[] { ConsA, ConsB }.Order(StringComparer.Ordinal))
        {
            bindings.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["consolidation"] = IriTerm(consolidation),
                ["consolidation_kind"] = LiteralTerm(kind),
                ["act"] = IriTerm(Act),
                ["multiplicity"] = LiteralTerm("1", XsdInteger),
                ["key_1"] = LiteralTerm(consolidation),
                ["key_2"] = LiteralTerm(kind),
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["head"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["link"] = Array.Empty<string>(),
                ["vars"] = projection.ToArray(),
            },
            ["results"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["distinct"] = false,
                ["ordered"] = true,
                ["bindings"] = bindings,
            },
        });
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");
}
