using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

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
        LuxembourgInitialDraftInventoryProducer.DecodeRows(rows, Profile(), Evidence);

    [TestMethod]
    public void ADeliveredSubjectBecomesAMemberCarryingItsOwnTerms()
    {
        var result = Decode(Row());

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);
        var subject = result.Subjects!.Single();
        Assert.AreEqual(Draft, subject.Value);
        Assert.AreEqual(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, subject.Kind);
        Assert.AreEqual(1, subject.Multiplicity);
        Assert.AreEqual(Evidence.ResourceId, subject.SourceObservationId);
        Assert.IsTrue(subject.IsAddressable);
    }

    /// <summary>
    /// A blank-node member is carried and typed, never filtered away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE OWNER RULING'S "refuse or type any non-addressable subject; it must not filter
    /// such a subject away", and it is why the query asks the class membership triple without an
    /// <c>isIRI</c> guard. Filtering at the query would make a member the publisher holds vanish
    /// from an inventory calling itself complete — the false absence S2-A03 forbids — and would do
    /// it invisibly, because the count would agree with the pages.
    /// </para>
    /// <para>
    /// It is not addressable: a blank-node label is scoped to the result set that produced it, so
    /// naming it in a later <c>VALUES</c> batch asks about nothing. That makes it a typed gap for
    /// the batching stage, which is a different thing from a member that does not exist.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABlankNodeMemberIsCarriedAndTypedRatherThanDropped()
    {
        var result = Decode(Row(), Row(draft: Blank("b0")));

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(2, result.Subjects!);

        var blank = result.Subjects!.Single(static value => !value.IsAddressable);
        Assert.AreEqual("b0", blank.Value);
        Assert.AreEqual(
            LuxembourgInitialDraftInventoryDiscoveryPlan.UnsupportedBlankNodeKind, blank.Kind);

        CollectionAssert.AreEqual(new[] { Draft }, result.AddressableInOrder().ToArray());
        Assert.HasCount(1, result.NonAddressable());
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
            Row(draft: Blank("b0")),
            Row(draft: Iri(Draft)));

        Assert.AreEqual(LuxembourgInitialDraftInventoryRefusal.None, result.Refusal, result.Detail);

        // Delivery order is preserved in Subjects and is NOT what the batch input uses: the batches
        // must be reproducible from the same inventory, and the publisher's order is not a promise.
        CollectionAssert.AreEqual(
            new[] { OtherDraft, "b0", Draft },
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
        Assert.ThrowsExactly<InvalidOperationException>(() => refused.NonAddressable());
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

    /// <summary>Two members, in the publisher's own wire shape, ordered by the keyset.</summary>
    private static string PageJson(IReadOnlyList<string> projection)
    {
        static Dictionary<string, string> IriTerm(string value) => new(StringComparer.Ordinal)
        {
            ["type"] = "uri",
            ["value"] = value,
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
        var bindings = new List<Dictionary<string, object>>();
        foreach (var subject in new[] { Draft, OtherDraft }.Order(StringComparer.Ordinal))
        {
            bindings.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["draft"] = IriTerm(subject),
                ["draft_kind"] = LiteralTerm(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind),
                ["multiplicity"] = LiteralTerm("1", XsdInteger),
                ["key_1"] = LiteralTerm(subject),
                ["key_2"] = LiteralTerm(LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind),
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
