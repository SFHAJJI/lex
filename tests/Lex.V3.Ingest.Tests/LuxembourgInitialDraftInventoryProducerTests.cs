using System.Text.Json;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8, the staged class sweep's first stage: which subjects the class actually holds.
/// </summary>
/// <remarks>
/// The batching stage that follows can only be complete with respect to something, and this is that
/// something. So the properties tested here are the ones the later stage rests on: every delivered
/// subject is carried, each arrives exactly once, a subject that cannot be addressed is TYPED rather
/// than dropped, and the list handed onward is deterministic.
/// </remarks>
[TestClass]
public sealed class LuxembourgInitialDraftInventoryProducerTests
{
    private const string InventoryFamily = "legilux-initial-draft-inventory";

    /// <summary>
    /// Rebinds rows onto the canonical keys of a real delivery, so the proof proves THESE rows.
    /// </summary>
    /// <remarks>
    /// The citation door re-derives the delivered rows' canonical-key digest and requires it to equal
    /// the proof's, because an honest proof of some other enumeration was found to authorize
    /// caller-chosen subjects. A fixture therefore cannot build rows and reach for a shared proof.
    /// Only the canonical key is replaced - the terms, which are all the producer reads, are exactly
    /// the ones each test wrote.
    /// </remarks>
    /// <summary>
    /// The run reference a delivery of this size carries, rebuilt independently of the decode.
    /// </summary>
    /// <remarks>
    /// Reconstructed rather than read back off the result, so the assertion compares the citation
    /// against the run the fixture actually proved instead of against another field of the same
    /// object.
    /// </remarks>
    private static SourceArtifactRef RunRefFor(int rowCount) =>
        AbsenceFixtures.Delivery(InventoryFamily, rowCount).Proof.AcquisitionRunRef;

    private static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRow[] Rows) Bound(
        string familyKey,
        IReadOnlyList<RepeatedEnumerationRow> rows)
    {
        var (proof, keys) = AbsenceFixtures.Delivery(familyKey, rows.Count);
        var bound = rows
            .Select((row, index) => new RepeatedEnumerationRow(row.Terms, keys[index], row.Cursor))
            .ToArray();
        return (proof, bound);
    }


    /// <summary>
    /// A REAL enumeration proof, because the citation doors now require one.
    /// </summary>
    /// <remarks>
    /// The run reference and the family key used to be handed to the producer as loose values, which
    /// is how a citation could state an identity instead of carrying one. Both now come off the
    /// proof, whose only door refuses anything but two independently agreeing, custody-verified
    /// passes. <c>AbsenceFixtures.Proof</c> is the same builder the contract tests use and is
    /// memoised, so this costs one assembly for the whole run rather than one per test.
    /// </remarks>

    private const string Draft = "http://data.legilux.public.lu/eli/etat/leg/projet/2019/03/14/a123/jo";
    private const string OtherDraft = "http://data.legilux.public.lu/eli/etat/leg/projet/2020/07/02/b456/jo";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:7d4b1e02-3f96-4a58-8c17-51e0ba62d94f", new string('c', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Blank(string label) =>
        RepeatedEnumerationRdfTerm.BlankNode(label);

    private static RepeatedEnumerationRdfTerm Literal(string value, string? datatype = null, string? language = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, language);

    private static string MarkerFor(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind,
        _ => LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind,
    };

    /// <summary>
    /// One delivered row, with every term, marker and key chosen independently.
    /// </summary>
    /// <remarks>
    /// Independence is the point. A builder that derived the marker and the keys from the subject
    /// could never express the deliveries this producer exists to refuse, and every disagreement
    /// guard would be unreachable from its own fixture. The defaults are coherent, so an honest row
    /// takes no arguments.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm? draft = null,
        string? draftKind = null,
        RepeatedEnumerationRdfTerm? draftKindTerm = null,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? key1 = null,
        string? key2 = null,
        RepeatedEnumerationRdfTerm? key1Term = null)
    {
        var subject = draft ?? Iri(Draft);
        var marker = draftKind ?? MarkerFor(subject);

        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            subject,
            draftKindTerm ?? Literal(marker),
            multiplicity ?? Literal("1", XsdInteger),
            key1Term ?? Literal(key1 ?? subject.Value ?? string.Empty),
            Literal(key2 ?? marker),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static LuxembourgInitialDraftInventoryResult Decode(params RepeatedEnumerationRow[] rows) =>
        DecodeBound(rows);

    private static LuxembourgInitialDraftInventoryResult DecodeBound(RepeatedEnumerationRow[] rows)
    {
        var (proof, bound) = Bound(InventoryFamily, rows);
        return LuxembourgInitialDraftInventoryProducer.DecodeRows(
            bound, Profile(), proof, "2026-09-10T13:50:31.0000000Z");
    }

    /// <summary>
    /// The inventory mints the citation a later batch must carry, from its own run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BATCH CITING AN INVENTORY IT ASSEMBLED ITSELF PROVES NOTHING. The citation was passed into
    /// the batch run by hand - in the canary, out of three environment variables - so nothing
    /// stopped it naming an inventory no run ever produced. Every field now comes from the run that
    /// enumerated the class.
    /// </para>
    /// <para>
    /// The digest is taken over the ADDRESSABLE population, which is what
    /// <c>AddressableInOrder</c> hands the batching stage, so it changes exactly when what the
    /// batches must cover changes - not when some other part of the delivery moves.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheInventoryMintsTheCitationABatchMustCarry()
    {
        const string SecondDraft = "http://data.legilux.public.lu/eli/dl/pl/2000/998";
        var result = Decode(Row(), Row(Iri(SecondDraft)));

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);

        var citation = result.Citation!;
        Assert.IsNotNull(citation, "a delivered inventory carries its own citation.");
        Assert.AreEqual("legilux-initial-draft-inventory", citation.FamilyKey);
        Assert.AreEqual(
            RunRefFor(2), citation.AcquisitionRunRef, "the run's own evidence, not a caller's.");
        Assert.AreEqual(result.AddressableInOrder().Count, citation.SubjectCount);
        Assert.IsNotEmpty(citation.ObservedAt, "an inventory a batch relies on must be datable.");

        // The digest is over the population, so a different population is a different citation.
        var narrower = Decode(Row());
        Assert.AreNotEqual(
            citation.SelectionDigest, narrower.Citation!.SelectionDigest,
            "a smaller inventory must not mint the same selection digest.");
    }

    /// <summary>
    /// The proven population cannot be edited through the list the producer built it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CODEX REPRODUCED THIS. <c>Subjects</c> was the producer's own <c>List</c> handed out behind an
    /// <c>IReadOnlyList</c>, which is a promise the type system does not keep: a caller could cast it
    /// back, add a draft the publisher never returned, and every batch would then be derived from the
    /// mutated list while the citation went on carrying the digest of the original. The population a
    /// cover reconciles against and the digest that identifies it would describe different classes.
    /// </para>
    /// <para>
    /// Asserted through the write path rather than by naming a type, so it stays a statement about
    /// what a caller can DO. Found unprotected by mutation: replacing the snapshot with the live list
    /// killed no test in either scope before this one existed.
    /// </para>
    /// <para>
    /// AND BOTH WRITE PATHS, because the first version of this test only closed one. It asked
    /// whether the collection reported itself writable and mutated it only if so, which made it a
    /// no-op against correct code and - worse - let the likeliest regression through: a bare
    /// <c>ToArray()</c> reports <c>IsReadOnly</c> true and refuses <c>Clear</c>, yet assigns happily
    /// through the <c>IList</c> indexer. That mutant survived. An unconditional refusal on each path
    /// is both the stronger statement and the shorter one.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheProvenPopulationCannotBeEditedThroughTheListItWasBuiltIn()
    {
        const string SecondDraft = "http://data.legilux.public.lu/eli/dl/pl/2000/998";
        var result = Decode(Row(), Row(Iri(SecondDraft)));

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);
        var before = result.AddressableInOrder().Count;
        var digest = result.Citation!.SelectionDigest;

        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<LuxembourgInitialDraftSubject>)result.Subjects!)[0] = result.Subjects![1],
            "a member cannot be replaced through the indexer.");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((ICollection<LuxembourgInitialDraftSubject>)result.Subjects!).Clear(),
            "and the population cannot be emptied.");

        Assert.AreEqual(
            before, result.AddressableInOrder().Count,
            "the population a batch is derived from is not editable by whoever holds the result.");
        Assert.AreEqual(
            digest, result.Citation!.SelectionDigest,
            "and the digest still identifies the population the run actually proved.");
    }

    /// <summary>A refused inventory carries no citation for anyone to lean on.</summary>
    [TestMethod]
    public void ARefusedInventoryMintsNoCitation()
    {
        var refused = Decode(Row(), Row());

        Assert.AreNotEqual(LuxembourgInitialDraftInventoryRefusal.None, refused.Refusal);
        Assert.IsNull(
            refused.Citation,
            "a batch must not be able to cite an inventory whose own enumeration was refused.");
    }

    [TestMethod]
    public void ADeliveredSubjectBecomesAMemberCarryingItsOwnTerms()
    {
        var result = Decode(Row());

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);
        var subject = result.Subjects!.Single();
        Assert.AreEqual(Draft, subject.Value);
        Assert.AreEqual(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, subject.Kind);
        Assert.AreEqual(1, subject.Multiplicity);
        Assert.AreEqual(RunRefFor(1).ResourceId, subject.SourceObservationId);
        Assert.IsTrue(subject.IsAddressable);
    }

    /// <summary>
    /// A blank-node member is reported, and it stops the inventory completing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST HEAD OF THIS SLICE TYPED IT AND CARRIED ON, and the review showed why that is not
    /// enough. The page derives <c>key_1</c> from <c>STR(?draft)</c>, which for a blank node is a
    /// label scoped to the result set that carried it — SPARQL's JSON results format says reuse of a
    /// label in another results object does not imply the same blank node. Source/Core refuses a
    /// canonical-key component whose RDF kind is a blank node, but here it never sees one: it sees
    /// the derived plain literal. Two passes could agree on <c>b0</c> while meaning different
    /// subjects, and the run still called itself complete.
    /// </para>
    /// <para>
    /// The ruling permits refusing OR typing a non-addressable subject and forbids filtering one
    /// away. Typing alone leaves the typed row inside a list that claims to be exact, and the whole
    /// point of this family is that a later stage covers that list exactly once. So the member is
    /// named on the refusal — which is what keeps this from being a silent filter — and no inventory
    /// is produced.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABlankNodeMemberIsReportedAndPreventsAnExactInventory()
    {
        var result = Decode(Row(), Row(draft: Blank("b0")));

        Assert.AreEqual(
            LuxembourgInitialDraftInventoryRefusal.NonAddressableSubjectObserved, result.Refusal);
        Assert.IsFalse(result.Delivered);
        Assert.IsNull(result.Subjects, "a list that cannot be exact is not handed over at all.");

        // REPORTED, NOT FILTERED. The member the publisher holds is named, with its kind, by a run
        // that declined to call itself complete.
        var observed = result.ObservedNonAddressable.Single();
        Assert.AreEqual("b0", observed.Value);
        Assert.AreEqual(
            LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind, observed.Kind);
        StringAssert.Contains(result.Detail!, "b0");
    }

    /// <summary>
    /// The reviewer's own reproduction: two passes agreeing on a label prove nothing about identity.
    /// </summary>
    /// <remarks>
    /// Driven through the live door rather than the decoder, because that is where it was found and
    /// because the two-pass agreement is exactly what made the delivery look trustworthy. Both
    /// passes return the same labels; nothing in the SPARQL results format makes them the same
    /// subjects.
    /// </remarks>
    [TestMethod]
    public async Task TwoPassesAgreeingOnBlankNodeLabelsDoNotProduceAnInventory()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var page = BlankNodePageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(2),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));

        var producer = new LuxembourgInitialDraftInventoryProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        var result = await producer.RunAsync(
            new LuxembourgInitialDraftInventoryRunRequest(
                plan,
                "urn:uuid:7c05e91a-2d38-4b64-8f17-90a3d51e6b2c",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9302)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsFalse(
            result.Delivered,
            "agreeing labels are not agreeing subjects, so this cannot be an exact inventory.");
        Assert.AreEqual(
            LuxembourgInitialDraftInventoryRefusal.NonAddressableSubjectObserved, result.Refusal);
        Assert.HasCount(2, result.ObservedNonAddressable);
    }

    /// <summary>
    /// RDF puts no literal in subject position, so a literal subject is a malformed delivery.
    /// </summary>
    /// <remarks>
    /// Typed and carried is for members this family cannot ADDRESS. A literal subject is not an
    /// exotic member; it is a row that cannot have come from <c>?draft a &lt;InitialDraft&gt;</c> at
    /// all, so admitting it would put something in the class that the publisher never said was in it.
    /// </remarks>
    [TestMethod]
    public void ASubjectOfAKindRdfCannotPutInSubjectPositionIsRefused()
    {
        var literal = Decode(Row(draft: Literal("not-a-subject"), draftKind: "iri"));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, literal.Refusal);

        var unbound = Decode(Row(draft: RepeatedEnumerationRdfTerm.Unbound(), draftKind: "iri"));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, unbound.Refusal);
    }

    /// <summary>The marker is read, and it is compared as a term rather than as a bare string.</summary>
    /// <remarks>
    /// The sibling opinion family shipped a projected marker nothing consumed, which let the
    /// delivered column contradict both its term and its key. Both halves are checked here: that the
    /// marker AGREES with the subject, and that it is the query's own unqualified plain literal, so
    /// an IRI-valued marker whose lexical form reads "iri" is refused — it did not come from this
    /// query, whose BIND is over string constants.
    /// </remarks>
    [TestMethod]
    public void AMarkerDisagreeingWithItsSubjectOrCarryingAQualifierRefusesTheRow()
    {
        var wrongForIri = Decode(Row(
            draftKind: LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, wrongForIri.Refusal);

        var wrongForBlank = Decode(Row(
            draft: Blank("b0"), draftKind: LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, wrongForBlank.Refusal);

        var iriShaped = Decode(Row(draftKindTerm: Iri("iri")));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, iriShaped.Refusal);

        var qualified = Decode(Row(draftKindTerm: Literal("iri", XsdString)));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, qualified.Refusal);
    }

    /// <summary>A key that does not key the terms its row delivered refuses the row.</summary>
    /// <remarks>
    /// The cursor orders by these, so a row keyed differently from what it delivered pages correctly
    /// and decodes into a different member. Both keys are checked, because a guard covering one
    /// would leave the other's whole value space unobserved.
    /// </remarks>
    [TestMethod]
    public void AKeyThatDoesNotKeyItsOwnTermsRefusesTheRow()
    {
        var wrongSubjectKey = Decode(Row(key1: OtherDraft));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, wrongSubjectKey.Refusal);

        var wrongKindKey = Decode(Row(
            key2: LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, wrongKindKey.Refusal);

        // The key is the query's own plain literal too: the plan binds it with COALESCE(STR(...)).
        var iriShapedKey = Decode(Row(key1Term: Iri(Draft)));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, iriShapedKey.Refusal);
    }

    /// <summary>The grouped count arrives as a positive xsd:integer, as COUNT(*) produces.</summary>
    /// <remarks>
    /// A plain literal here is what a fixture writes and no engine sends. Checking the digits and
    /// not the datatype is the hole the sibling procedure-event producer was found to have, where a
    /// positive <c>xsd:string</c> was accepted as a count.
    /// </remarks>
    [TestMethod]
    public void TheGroupedCountMustBeAPositiveInteger()
    {
        foreach (var wrong in new[]
        {
            Literal("1"),
            Literal("1", XsdString),
            Literal("0", XsdInteger),
            Literal("-1", XsdInteger),
            Literal("not-a-number", XsdInteger),
        })
        {
            var result = Decode(Row(multiplicity: wrong));
            Assert.AreEqual(
                LuxembourgInitialDraftInventoryRefusal.RowNotAdmitted, result.Refusal,
                $"multiplicity {wrong.Value}/{wrong.Datatype} is not a grouped count.");
        }

        var honest = Decode(Row(multiplicity: Literal("3", XsdInteger)));
        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, honest.Refusal, honest.Detail);
        Assert.AreEqual(3, honest.Subjects!.Single().Multiplicity);
    }

    /// <summary>
    /// A subject delivered twice is refused rather than quietly deduplicated.
    /// </summary>
    /// <remarks>
    /// The query groups by the subject, so an honest answer names each exactly once. This is the
    /// property the batching stage rests on — every inventoried subject in exactly one batch — and
    /// deduplicating here would make "exactly one" ambiguous before batching began, while hiding a
    /// publisher answer that did not match the question asked.
    /// </remarks>
    [TestMethod]
    public void ASubjectDeliveredTwiceIsRefused()
    {
        var result = Decode(Row(), Row());

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.SubjectDeliveredTwice, result.Refusal);
        StringAssert.Contains(result.Detail!, Draft);
    }

    /// <summary>The list handed to the batching stage is deterministic and excludes what it cannot name.</summary>
    [TestMethod]
    public void TheBatchInputIsOrdinalOrderedAndAddressableOnly()
    {
        var result = Decode(
            Row(draft: Iri(OtherDraft)),
            Row(draft: Iri(Draft)));

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);

        // Delivery order is preserved in Subjects and is NOT what the batch input uses: the batches
        // must be reproducible from the same inventory, and the publisher's order is not a promise.
        CollectionAssert.AreEqual(
            new[] { OtherDraft, Draft },
            result.Subjects!.Select(static value => value.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { Draft, OtherDraft }.Order(StringComparer.Ordinal).ToArray(),
            result.AddressableInOrder().ToArray());
    }

    /// <summary>A refused inventory has no subjects, and asking for them throws rather than answering empty.</summary>
    /// <remarks>
    /// An empty list would be the false absence in its most convenient disguise: a caller that
    /// batched a refused inventory would cover zero subjects and report success.
    /// </remarks>
    [TestMethod]
    public void ARefusedInventoryRefusesToHandOverABatchInput()
    {
        var refused = Decode(Row(draft: Literal("not-a-subject"), draftKind: "iri"));

        Assert.IsFalse(refused.Delivered);
        Assert.IsNull(refused.Subjects);
        Assert.ThrowsExactly<InvalidOperationException>(() => refused.AddressableInOrder());
        Assert.IsEmpty(
            refused.ObservedNonAddressable,
            "this refusal is about a malformed row, not about a member no request can name.");
    }

    /// <summary>An empty class is a complete answer about an empty class.</summary>
    [TestMethod]
    public void AClassWithNoMembersIsAnAnswerRatherThanARefusal()
    {
        var result = Decode();

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.Subjects!);
        Assert.IsEmpty(result.AddressableInOrder());
    }

    /// <summary>
    /// The family runs end to end and the members cite the run's OWN evidence.
    /// </summary>
    /// <remarks>
    /// Every other test here calls the internal decoder with rows a test built and an evidence
    /// reference a test invented. That proves the decoding and proves nothing about whether this
    /// family can be run at all. The cited artifact is read back out of custody and required to name
    /// its own schema, rather than being compared against the producer's own report of it — a
    /// mutation citing one request's HTTP evidence instead of the acquisition run survived exactly
    /// that weaker assertion on the sibling E6 family.
    /// </remarks>
    [TestMethod]
    public async Task TheFamilyRunsEndToEndAndCitesTheRunsOwnEvidence()
    {
        var plan = LuxembourgInitialDraftInventoryDiscoveryPlan.Create();
        var page = PageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(2),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgInitialDraftInventoryProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            new LuxembourgInitialDraftInventoryRunRequest(
                plan,
                "urn:uuid:5e1a9c37-84b0-4d26-9f75-3c60817ae4d2",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9301)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(2, result.Subjects!);
        Assert.IsGreaterThan(0, result.ProductRequestCount);

        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            System.Text.Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the members cite the acquisition RUN, not one request's HTTP evidence.");

        CollectionAssert.AreEqual(
            new[] { Draft, OtherDraft }.Order(StringComparer.Ordinal).ToArray(),
            result.AddressableInOrder().ToArray());
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9301);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:6f2c8b41-9d05-4a73-bc18-2e947d0f5a36",
            "urn:uuid:8a3d5e29-1b74-4c60-9e82-5f0316bd47c9",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9301)).Request;
    }

    /// <summary>Two blank-node members, labelled exactly as a second response would label them.</summary>
    private static string BlankNodePageJson(IReadOnlyList<string> projection) =>
        PageJson(projection, blankNodes: true);

    /// <summary>Two members, in the publisher's own wire shape, ordered by the keyset.</summary>
    private static string PageJson(IReadOnlyList<string> projection, bool blankNodes = false)
    {
        static Dictionary<string, string> IriTerm(string value) => new(StringComparer.Ordinal)
        {
            ["type"] = "uri",
            ["value"] = value,
        };

        static Dictionary<string, string> BlankTerm(string label) => new(StringComparer.Ordinal)
        {
            ["type"] = "bnode",
            ["value"] = label,
        };

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

        // ORDERED BY THE KEYSET, which is what the page is ordered by and what the executor advances
        // on. A page delivered out of order refuses with CursorDidNotAdvance.
        var subjects = blankNodes
            ? new[] { "b0", "b1" }
            : new[] { Draft, OtherDraft }.Order(StringComparer.Ordinal).ToArray();
        var kind = blankNodes
            ? LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind
            : LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind;

        var bindings = new List<Dictionary<string, object>>();
        foreach (var subject in subjects)
        {
            bindings.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["draft"] = blankNodes ? BlankTerm(subject) : IriTerm(subject),
                ["draft_kind"] = LiteralTerm(kind),
                ["multiplicity"] = LiteralTerm("1", XsdInteger),

                // key_1 IS THE DERIVED PLAIN LITERAL, exactly as STR(?draft) produces it — which for
                // a blank node is the result-scoped label. That is the shape the defect lived in:
                // Source/Core's refusal of a blank-node canonical key never sees a blank node here.
                ["key_1"] = LiteralTerm(subject),
                ["key_2"] = LiteralTerm(kind),
            });
        }

        // THE PUBLISHER'S ENVELOPE, not a convenient subset of it. Source/Core requires head to
        // carry link AND vars, and results to carry distinct, ordered AND bindings, because that is
        // what this Virtuoso sends; a fixture writing less is describing a response no engine
        // produces, and the first version of this one did exactly that.
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
}
