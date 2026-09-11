using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8, the Luxembourg draft graph: the plan, the executor entry and the producer that
/// turn delivered <c>InitialDraft</c> property rows into records.
/// </summary>
/// <remarks>
/// The admit path is asserted first and end to end. Before this family existed,
/// <c>LuxembourgDraftRelationPredicate</c> was a closed vocabulary reached by nothing — this
/// repository declared <c>draftTransposes</c> and never asked the publisher about it — and two
/// sibling families reached integration whose decoders no code called.
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphProducerTests
{

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
    private const string InventoryFamily = "legilux-initial-draft-inventory";

    /// <summary>
    /// Rebinds rows onto the canonical keys of a real delivery, so the proof proves THESE rows.
    /// </summary>
    /// <remarks>
    /// The citation doors re-derive the delivered rows' canonical-key digest and require it to equal
    /// the proof's, because an honest proof of some other enumeration was found to authorize
    /// caller-chosen subjects. Only the canonical key is replaced; the terms, which are all the
    /// producer reads, stay exactly as each test wrote them.
    /// </remarks>
    /// <summary>The run reference a delivery of this size carries, rebuilt independently.</summary>
    private static SourceArtifactRef RunRefFor(int rowCount) =>
        AbsenceFixtures.Delivery(InventoryFamily, rowCount).Proof.AcquisitionRunRef;

    private static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRow[] Rows) Bound(
        string familyKey,
        IReadOnlyList<RepeatedEnumerationRow> rows)
    {
        // The inventory door derives its proven population from the first key component, so the key
        // must be the subject each row decodes to - which is its first term.
        var subjects = rows.Select(static row => row.Terms[0].Value ?? string.Empty).ToArray();

        // A delivery that repeats a subject cannot be keyed on subjects at all - canonical keys must
        // be unique - and it is not an enumeration either: the producer refuses it before any
        // citation is minted, so the door this keying exists for is never reached. Those fixtures
        // keep positional keys, which is the honest description of a delivery that proves nothing.
        // A delivery that repeats a subject, or delivers out of key order, cannot be proven at all:
        // Source/Core requires canonical keys unique and cursors strictly increasing. Those are
        // exactly the deliveries the producer refuses before any citation is minted, so they keep
        // positional keys - an honest description of a delivery that proves nothing about subjects.
        var sortedUnique = subjects
            .OrderBy(static value => value, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (!subjects.SequenceEqual(sortedUnique, StringComparer.Ordinal))
        {
            var (refusedProof, refusedKeys) = AbsenceFixtures.Delivery(familyKey, rows.Count);
            return (refusedProof, rows
                .Select((row, index) => new RepeatedEnumerationRow(row.Terms, refusedKeys[index], row.Cursor))
                .ToArray());
        }

        // ROW ORDER IS PRESERVED, never rearranged to suit the fixture. An earlier version sorted
        // the rows to line them up with the keys, which silently changed what a test observed about
        // delivery order. A proven delivery is necessarily key-ordered - Source/Core requires
        // cursors to strictly increase - so a fixture wanting a proof must deliver in that order,
        // and saying so out loud is better than quietly reordering behind the test.
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(familyKey, subjects);
        var bound = rows
            .Select((row, index) => new RepeatedEnumerationRow(row.Terms, keys[index], row.Cursor))
            .ToArray();
        return (proof, bound);
    }
    private const string Draft = "http://data.legilux.public.lu/resource/draft/8357";
    private const string OtherDraft = "http://data.legilux.public.lu/resource/draft/8358";
    private const string Directive = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string DossierPage = "https://www.chd.lu/fr/dossier/8357";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdAnyUri = "http://www.w3.org/2001/XMLSchema#anyURI";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:5f28c063-9a41-4b7e-8d05-c31742ef96ba", new string('d', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        LuxembourgDraftGraphDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Literal(string value, string? datatype = null, string? language = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, language);

    private static RepeatedEnumerationRdfTerm Unbound() => RepeatedEnumerationRdfTerm.Unbound();

    /// <summary>
    /// One delivered row, with every term, marker, qualifier column and cursor key derived from the
    /// term unless a test overrides exactly one.
    /// </summary>
    /// <remarks>
    /// Coherent by construction, so an honest row needs no arranging and a contradiction has to be
    /// asked for. A fixture that hard-codes what it is meant to be varying cannot express the
    /// deliveries a producer exists to refuse — three tests in the sibling procedure-event file
    /// passed for the wrong reason before its fixture was built this way.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        string draft = Draft,
        string? predicate = null,
        RepeatedEnumerationRdfTerm? value = null,
        string? draftKind = null,
        string? valueKind = null,
        string? datatype = null,
        string? language = null,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? key1 = null,
        string? key2 = null,
        string? key3 = null,
        string? key4 = null,
        string? key5 = null,
        string? key6 = null,
        string? key7 = null)
    {
        var draftTerm = Iri(draft);
        var predicateIri = predicate ?? LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri;
        var valueTerm = value ?? Iri(Directive);
        var datatypeColumn = datatype ?? DatatypeColumnFor(valueTerm);
        var languageColumn = language ?? Qualifier(valueTerm, static term => term.Language);

        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            draftTerm,
            Literal(draftKind ?? Marker(draftTerm)),
            Iri(predicateIri),
            valueTerm,
            Literal(valueKind ?? Marker(valueTerm)),
            Literal(datatypeColumn),
            Literal(languageColumn),
            multiplicity ?? Literal("1", XsdInteger),
            Literal(key1 ?? draft),
            Literal(key2 ?? Marker(draftTerm)),
            Literal(key3 ?? predicateIri),
            Literal(key4 ?? valueTerm.Value ?? string.Empty),
            Literal(key5 ?? Marker(valueTerm)),
            Literal(key6 ?? datatypeColumn),
            Literal(key7 ?? languageColumn),
        };

        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static string Marker(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        _ => LuxembourgDraftGraphDiscoveryPlan.UnboundKind,
    };

    private static string Qualifier(
        RepeatedEnumerationRdfTerm term, Func<RepeatedEnumerationRdfTerm, string?> select) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal ? select(term) ?? string.Empty : string.Empty;

    /// <summary>The datatype column as THIS ENGINE answers it, not as the term spells it.</summary>
    /// <remarks>
    /// Measured on the first broad delivery. SPARQL JSON omits <c>datatype</c> for a plain literal,
    /// but <c>DATATYPE()</c> answers it with <c>xsd:string</c>, because under RDF 1.1 a simple
    /// literal IS an xsd:string. A fixture that mirrored the term instead would model a delivery
    /// this publisher does not send - and mirroring is precisely the conflation that let a real
    /// forty-row delivery be refused.
    /// </remarks>
    private static string DatatypeColumnFor(RepeatedEnumerationRdfTerm term) =>
        term.Kind != RepeatedEnumerationRdfTermKind.Literal ? string.Empty
        : term.Datatype is { Length: > 0 } declared ? declared
        : term.Language is { Length: > 0 } ? string.Empty
        : "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>Decodes exactly the rows given.</summary>
    /// <remarks>
    /// This used to complete every draft with unbound rows for the properties a test did not name,
    /// because the query asked for the absent case and an honest delivery carried a row per pair.
    /// It does not any more: the value triple is mandatory, so a delivery naming one property IS
    /// honest and the pairs with no row are answered by derived absences instead. The completion
    /// was also the reason no test in this suite could ever see that the absence branch was inert -
    /// every fixture authored its own absence rows by hand, so the delivery a test examined was one
    /// the publisher had never sent.
    /// </remarks>
    /// <summary>
    /// An accepted production cannot be edited into asserting a predicate it does not admit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FALSE-ABSENCE DOOR THIS CLOSES. <c>Records</c>, <c>AdmittedPredicates</c> and
    /// <c>RetainedNotAdmitted</c> were published through read-only interfaces over collections the
    /// producer had built and still held, and <c>For()</c> reads two of them live. Casting the
    /// admitted set back to its <c>HashSet</c> and adding a predicate this family does not admit
    /// walks straight past the <c>ArgumentOutOfRangeException</c> that exists to say so, and
    /// <c>For()</c> then answers with an empty record list - an absence manufactured after the
    /// production was accepted, which is exactly what S2-A03 forbids.
    /// </para>
    /// <para>
    /// Found by an audit of the aliasing surface rather than reported, and it is the same defect the
    /// reviewer measured one layer up on the coverage. Fixing only the layer that was reported would
    /// have left this one open.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnAcceptedProductionCannotBeEditedIntoAssertingWhatItDoesNotAdmit()
    {
        var result = Decode(Row());
        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);

        const string NotAdmitted = "http://example.invalid/predicate-this-family-never-admits";
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.For(NotAdmitted),
            "a predicate this family does not admit has no assertion to give.");

        Assert.IsFalse(
            result.AdmittedPredicates is ICollection<string> { IsReadOnly: false },
            "the admitted set must not be writable through a downcast.");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<LuxembourgDraftPropertyRecord>)result.Records!)[0] = result.Records![0],
            "nor may the records be repointed after admission.");
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((ICollection<LuxembourgDraftRetainedEvidenceRow>)result.RetainedNotAdmitted).Clear(),
            "nor may the retained evidence be emptied.");

        // And the door still refuses, which is what the mutation above was trying to get past.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => result.For(NotAdmitted));
    }

    private static LuxembourgDraftGraphProductionResult Decode(params RepeatedEnumerationRow[] rows)
    {
        // AN INVENTORY THAT ACTUALLY CONTAINS THESE DRAFTS. A fixture can no longer decode a batch
        // no inventory issued, which is the whole of the repair: the members and the citation come
        // from one place or the coverage cannot be built at all.
        var drafts = RequestedIn(rows);
        var assignment = LuxembourgDraftGraphBatchFactory.AssignBatches(InventoryOf([.. drafts]))[0];

        // The partition a batch citation names must be the one its proof proves.
        var (proof, bound) = Bound(assignment.PartitionKey, rows);

        return LuxembourgDraftGraphProducer.DecodeRows(
            bound, Profile(), proof, assignment,
            assignment.PartitionKey,
            "2026-09-10T13:50:31.0000000Z");
    }

    /// <summary>The drafts a fixture delivery names, as the set it asked about.</summary>
    /// <remarks>
    /// Only honest for a fixture that means its delivery to be complete. A test that needs the
    /// requested set and the delivered set to DISAGREE says so by calling the producer directly;
    /// the coverage guards themselves are proven in
    /// <c>LuxembourgDraftPropertyCoverageTests</c>, where both sides are chosen independently.
    /// </remarks>
    private static IReadOnlyList<string> RequestedIn(IReadOnlyList<RepeatedEnumerationRow> rows)
    {
        var ordinal = Profile().ProjectionVariables.ToList().IndexOf("draft");
        return LuxembourgDraftGraphDiscoveryPlan.RequestedPartitionMembers(
            rows.Select(row => row.Terms[ordinal].Value ?? Draft).DefaultIfEmpty(Draft).ToArray());
    }

    /// <summary>A delivered inventory over exactly the named drafts.</summary>
    /// <remarks>
    /// A batch run can no longer be handed a draft list and an unrelated inventory, so a test that
    /// wants to sweep a draft has to prove an inventory containing it. That is the point of the
    /// change rather than an inconvenience of it: the pairing this used to allow is what let a run
    /// mint absences for a subject the proven population never contained.
    /// </remarks>
    private static LuxembourgInitialDraftInventoryResult InventoryOf(params string[] drafts)
    {
        var profile = LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile();
        var rows = drafts.Select(draft =>
        {
            var terms = new List<RepeatedEnumerationRdfTerm>
            {
                RepeatedEnumerationRdfTerm.Iri(draft),
                RepeatedEnumerationRdfTerm.Literal(
                    LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    "1", "http://www.w3.org/2001/XMLSchema#integer", null),
                RepeatedEnumerationRdfTerm.Literal(draft, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
            };
            return new RepeatedEnumerationRow(terms, terms, terms);
        }).ToArray();

        var (proof, bound) = Bound(InventoryFamily, rows);
        return LuxembourgInitialDraftInventoryProducer.DecodeRows(
            bound, profile, proof, "2026-09-10T07:29:37.8950843Z");
    }



    /// <summary>
    /// The whole chain runs: executor, two passes, proof, reopened pages, verified rows, records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE GUARD THAT MAKES THE PUBLIC PATH REAL. Every other test here calls the internal decoder
    /// with rows a test built. That proves the decoding and proves nothing about whether the family
    /// can be run — and Codex demonstrated exactly that on the first head of this slice by making
    /// <see cref="LuxembourgDraftGraphProducer.RunAsync"/> throw for any request: the eleven tests
    /// still passed. A slice whose stated point is that the producer owns its run must drive the run.
    /// </para>
    /// <para>
    /// The cited artifact has to name its own schema, read back out of custody, rather than being
    /// compared against the producer's own report of it. That weaker assertion is self-referential —
    /// both sides come from the same value — and a mutation citing one request's HTTP evidence
    /// instead of the acquisition run survived it on the procedure-event family.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheFamilyRunsEndToEndAndCitesTheRunsOwnEvidence()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var page = PageJson(plan.CreateDeliveryProfile().ProjectionVariables);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, request) =>
            LuxembourgAcquisitionTestFixture.JsonResponse(request, ordinal switch
            {
                1 or 3 => LuxembourgAcquisitionTestFixture.CountJson(
                    LuxembourgDraftGraphDiscoveryPlan.AskedAbout.Count),
                2 or 4 => page,
                _ => throw new AssertFailedException("No request is admitted after both passes complete."),
            }));

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new LuxembourgDraftGraphProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            LuxembourgDraftGraphRunRequest.ForBatch(
                plan,
                InventoryOf(Draft),
                0,
                "urn:uuid:1a7c5e39-4b62-4d80-9f13-6e025ac84b71",
                LuxembourgAcquisitionTestFixture.BuildRendererSource(9101)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        // ADMITTED, NOT ACCEPTED. The fixture delivers a row for every accepted predicate, and one
        // of them - referralDate - is declared on OpinionRequest, so a triple asserting it of a
        // draft is drift: retained with its shape, never a record.
        Assert.HasCount(
            LuxembourgDraftGraphDiscoveryPlan.DirectlyAdmissiblePredicates.Count, result.Records!,
            "every admissible property the delivery answered becomes a record.");

        var drift = result.RetainedNotAdmitted.Single();
        Assert.AreEqual(LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri, drift.PredicateIri);
        Assert.AreEqual(
            LuxembourgDraftRetentionReason.PredicateDeclaredOnAnotherClass, drift.Reason,
            "an accepted predicate on the wrong class is drift, not an unknown predicate.");
        Assert.AreEqual(
            LuxembourgDraftAcquiredScope.EveryPredicateOnTheSubject, result.AcquiredScope,
            "the run reports what it asked for, not what it admits.");
        Assert.IsGreaterThan(0, result.ProductRequestCount);

        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            System.Text.Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the records cite the acquisition RUN, not one request's HTTP evidence.");

        Assert.HasCount(
            1, result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri));
    }

    /// <summary>
    /// A draft answered on some properties and not others is the ordinary case, not a partial one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE INVERSION THIS SHAPE FORCED, and it is measured. This test previously refused such a
    /// delivery, on the reasoning that the query's absence branch guaranteed a row per pair. That
    /// branch was found permanently inert and then removed: the publisher will not serve a query
    /// that materialises absent pairs, and asking it to would have made it assert something it
    /// never said. The mandatory value triple delivers a row per value HELD.
    /// </para>
    /// <para>
    /// So fewer than five rows per draft is what an honest delivery looks like. The retained
    /// fifty-draft delivery carried 95 present pairs out of 250: had this refusal survived, the
    /// first real batch would have been rejected as incomplete. The pairs with no row are not lost
    /// - they become derived absences in <see cref="LuxembourgDraftPropertyCoverage"/>, which is
    /// where completeness is now proven, over the requested set rather than over the answer.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ADraftAnsweredOnOnlySomePropertiesIsAdmitted()
    {
        var result = Decode(
            Row(predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Literal("en-cours")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Records!);
        Assert.IsEmpty(
            result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri),
            "a property with no delivered row simply has no record here; its absence is derived "
                + "against the requested batch, not read out of the delivery.");
    }

    /// <summary>
    /// An unbound value refuses the delivery; a missing row is ordinary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXACTLY INVERTED FROM WHAT THIS TEST ONCE ASSERTED, and the inversion is the design. The
    /// value triple is mandatory, so no solution mapping can leave the value unbound: a row that
    /// does is a delivery this plan cannot have produced, and the page is not trusted.
    /// </para>
    /// <para>
    /// Admitting it is the specific thing the ruling forbids. Such a row would become a record that
    /// LOOKS like the publisher reporting an absence, when the publisher said no such thing - an
    /// absence is this code's conclusion from a complete enumeration, and it must be typed as one.
    /// A missing row, meanwhile, is not a defect at all: it is how the publisher says nothing, and
    /// it is answered by a derived absence rather than by a refusal.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnUnboundValueRefusesTheDeliveryAndAMissingRowIsOrdinary()
    {
        var unbound = Decode(
            Row(predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Unbound()));
        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, unbound.Refusal,
            "the publisher cannot answer a mandatory triple with nothing.");

        var held = Decode(
            Row(predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Literal("en-cours")));
        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.None, held.Refusal,
            "and the four properties with no row at all are ordinary, not a refusal.");
        Assert.HasCount(1, held.Records!);
    }

    /// <summary>A delivery with no drafts at all is a complete answer about an empty class.</summary>
    [TestMethod]
    public void ADeliveryWithNoDraftsIsACompleteAnswerRatherThanAnIncompleteOne()
    {
        var result = Decode();

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsEmpty(result.Records!);
    }

    /// <summary>
    /// A language-tagged value arrives with its datatype column absent, and that is admitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PUBLISHER'S OWN ENCODING, measured and retained. For a language-tagged literal this
    /// engine does not answer <c>DATATYPE()</c> with <c>rdf:langString</c>, so the BIND errors and
    /// the column is omitted from the binding entirely —
    /// <c>EuPageDecodeClassificationTests</c> holds the page where 32 of 373 rows were that shape.
    /// </para>
    /// <para>
    /// The first head of this slice required both qualifier columns to be plain literals and
    /// therefore refused every language-tagged value, which for <c>statusDraft</c> is the ordinary
    /// case. Nothing is lost by the column reading empty: <c>language_tag</c> is non-empty for
    /// exactly these rows.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ALanguageTaggedValueWhoseDatatypeColumnIsAbsentIsAdmitted()
    {
        var profile = Profile();
        var row = Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
            value: Literal("en-cours", null, "fr"));

        // The publisher's wire shape: the column is not empty, it is ABSENT.
        var terms = row.Terms.ToList();
        terms[profile.ProjectionVariables.ToList().IndexOf("datatype_iri")] =
            RepeatedEnumerationRdfTerm.Unbound();

        var result = Decode(new RepeatedEnumerationRow(terms, terms, terms));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        var record = result.Records!.Single(value =>
            value.PredicateIri == LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri);
        Assert.AreEqual(string.Empty, record.ValueDatatypeIri);
        Assert.AreEqual("fr", record.ValueLanguageTag, "the language tag is what identifies it.");
    }

    /// <summary>
    /// The one admitted absence does not extend to the other column, to another kind of term, or to
    /// the query's own absence branch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST REPAIR WAS TOO WIDE AND THE REVIEW CAUGHT IT. Reading any unbound qualifier as the
    /// empty string admitted an IRI-valued row with either column simply missing, because that row's
    /// expected qualifier is empty anyway and its totalised cursor key is empty either way — so
    /// "the publisher answered empty" and "nothing arrived" became the same row, which is the
    /// evidence ambiguity this family exists to refuse.
    /// </para>
    /// <para>
    /// The measurement says which of those the publisher actually does. Both columns are bound by
    /// <c>IF(isLiteral(?value), ..., "")</c>, which answers an empty plain literal for a term that is
    /// not a literal, and the retained page bears it out: of its 41 bindings 23 are IRI-valued and
    /// <c>datatype_iri</c> is present and empty in every one. The absence branch binds both columns
    /// to <c>""</c> outright. So the only honest absence is <c>datatype_iri</c> on a LANGUAGE-TAGGED
    /// literal, and each of these rows is otherwise coherent — one column removed, nothing else.
    /// </para>
    /// </remarks>
    [TestMethod]
    [DataRow("datatype_iri", "iri", DisplayName = "an IRI value must answer its datatype column")]
    [DataRow("language_tag", "iri", DisplayName = "an IRI value must answer its language column")]
    [DataRow("datatype_iri", "unbound", DisplayName = "the absence branch binds its datatype column")]
    [DataRow("language_tag", "unbound", DisplayName = "the absence branch binds its language column")]
    [DataRow("datatype_iri", "plain", DisplayName = "a typed literal must answer its datatype column")]
    [DataRow("datatype_iri", "bare", DisplayName = "an untagged literal must answer its datatype column")]
    [DataRow("language_tag", "langString", DisplayName = "a language-tagged literal still answers LANG")]
    public void AMissingQualifierIsRefusedOutsideTheOneMeasuredException(string column, string shape)
    {
        var value = shape switch
        {
            "iri" => Iri(Directive),
            "unbound" => RepeatedEnumerationRdfTerm.Unbound(),
            "plain" => Literal("2024-07-11", XsdDate),
            // NO datatype and NO language, so the expected qualifier is empty and the delivered
            // column would be empty too. That is the shape where a too-loose exception hides: every
            // OTHER kind of value is caught by the column/term comparison a step later, which is why
            // dropping the language check from the exception left every other row still refused.
            // This publisher is not measured sending bare literals - the retained pages type every
            // string as xsd:string - but the guard cannot rest on the publisher's habits.
            "bare" => Literal("en-cours"),
            _ => Literal("en-cours", null, "fr"),
        };

        var result = Decode(WithoutColumn(
            Row(
                predicate: shape == "iri"
                    ? LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri
                    : LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: value),
            column));

        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal,
            $"a missing {column} on a {shape} value is not this query's answer: {result.Detail}");
    }

    /// <summary>
    /// The admitted exception is keyed on the VALUE, not on the columns, so a row cannot claim it by
    /// dropping both.
    /// </summary>
    /// <remarks>
    /// Keying it on the language COLUMN would have been the obvious reading and is circular: a row
    /// that omitted both columns would present as "no language, so no exception" or as "empty
    /// language" depending on which column was consulted first. The term carries the tag itself, and
    /// SPARQL JSON delivers it on the value whether or not <c>LANG()</c> was projected.
    /// </remarks>
    [TestMethod]
    public void ARowDroppingBothQualifiersCannotClaimTheLanguageTaggedException()
    {
        var row = WithoutColumn(
            WithoutColumn(
                Row(
                    predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                    value: Literal("en-cours", null, "fr")),
                "datatype_iri"),
            "language_tag");

        var result = Decode(row);

        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal,
            $"LANG() answers on this term, so the language column cannot be missing: {result.Detail}");
    }

    /// <summary>One projected column removed from an otherwise coherent row, as the wire drops it.</summary>
    /// <remarks>
    /// The cursor keys are deliberately left alone. SPARQL JSON omits an unbound variable from the
    /// binding, and the totalised key derived from it still arrives as the empty string, so a row
    /// with the column gone and the key present is exactly what this engine sends.
    /// </remarks>
    private static RepeatedEnumerationRow WithoutColumn(RepeatedEnumerationRow row, string column)
    {
        var terms = row.Terms.ToList();
        terms[Profile().ProjectionVariables.ToList().IndexOf(column)] =
            RepeatedEnumerationRdfTerm.Unbound();
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    /// <summary>A delivered transposition intention becomes a record carrying its exact terms.</summary>
    [TestMethod]
    public void ADeliveredTranspositionBecomesARecord()
    {
        var result = Decode(Row());

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);

        // The delivery answers all five properties; this test is about the transposition row.
        var record = result
            .For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri).Single();
        Assert.AreEqual(Draft, record.DraftIri);
        Assert.AreEqual(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri, record.PredicateIri);
        Assert.AreEqual(Directive, record.Value);
        Assert.AreEqual("iri", record.ValueKind);
        Assert.AreEqual(RunRefFor(1).ResourceId, record.SourceObservationId);
    }

    /// <summary>
    /// A property the publisher holds nothing for produces no row, and no record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CLAIM THIS TEST ONCE MADE WAS TRUE OF A QUERY THAT NEVER WORKED. It asserted that "this
    /// draft transposes nothing" arrived as a row carrying the unbound marker, because the plan
    /// asked for the absence by name. That branch was found permanently inert - the first bounded
    /// batch ever sent came back with 103 rows and not one unbound marker - and every variant that
    /// would have made it fire timed the publisher out.
    /// </para>
    /// <para>
    /// So the gap does not arrive as a row, and this producer no longer pretends it can. What S2-A03
    /// requires is that the gap be first-class, not that the publisher utter it: the pair becomes a
    /// derived absence in <see cref="LuxembourgDraftPropertyCoverage"/>, typed so it can never be
    /// read as something Legilux said. What must NOT happen is the delivery being admitted with a
    /// fabricated unbound row, and that is what this now asserts.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void APropertyTheDraftHoldsNothingForProducesNoRowAtAll()
    {
        var result = Decode(Row(value: Unbound()));

        Assert.AreEqual(
            LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal,
            "an unbound value under a mandatory triple is a delivery this plan cannot have made.");
        StringAssert.Contains(result.Detail!, "mandatory");
    }

    /// <summary>
    /// A literal value keeps its datatype and its language, because those are what tell two apart.
    /// </summary>
    /// <remarks>
    /// Two literals sharing a lexical form and differing in datatype are different facts — the rule
    /// two review rounds settled on the procedure-event plan. A record keeping only the lexical form
    /// could not distinguish them after the fact even though the delivery could.
    /// </remarks>
    [TestMethod]
    public void ALiteralValueKeepsItsDatatypeAndLanguage()
    {
        // Uses an ADMISSIBLE predicate. This once used referralDate, which is declared on
        // OpinionRequest: a direct triple for it is drift and is retained rather than admitted, so
        // the test was pinning an admission that must not happen.
        var dated = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
            value: Literal("2024-05-22", XsdDate)));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, dated.Refusal, dated.Detail);
        Assert.AreEqual(XsdDate, dated.Records![0].ValueDatatypeIri);
        Assert.AreEqual(string.Empty, dated.Records![0].ValueLanguageTag);

        var tagged = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
            value: Literal("en-cours", null, "fr")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, tagged.Refusal, tagged.Detail);
        Assert.AreEqual("fr", tagged.Records![0].ValueLanguageTag);
    }

    /// <summary>
    /// A dossier URL is retained as text. Nothing here dereferences it.
    /// </summary>
    /// <remarks>
    /// <c>parliamentDraftUrl</c> names a page on a host this pipeline does not contact. Retaining a
    /// URL is not requesting one; this asserts the value survives as a string and, by the absence of
    /// any fetch in this family, that it is never followed.
    /// </remarks>
    [TestMethod]
    public void ADossierUrlIsRetainedAsTextAndNotFollowed()
    {
        var result = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.ParliamentDraftUrlPredicateIri,
            value: Literal(DossierPage, XsdAnyUri)));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.AreEqual(DossierPage, result.Records![0].Value);
        Assert.AreEqual(XsdAnyUri, result.Records![0].ValueDatatypeIri);
    }

    /// <summary>
    /// A property this family does not admit is retained as evidence, not refused and not a fact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// INVERTED BY MEASUREMENT. This test used to refuse such a row, on the reasoning that the plan
    /// bound its five predicates from a VALUES block so an honest delivery could not contain
    /// another. The VALUES block is gone: it was measured DROPPING rows the publisher holds -
    /// zero parliamentDraftUrl for fifty drafts where ten of them carry one - and every dropped pair
    /// was being minted as a derived absence.
    /// </para>
    /// <para>
    /// So the publisher is now asked for every predicate it holds about these subjects and
    /// admission is made here. A predicate outside the accepted five is named on
    /// <c>RetainedNotAdmitted</c> and becomes no record: this family asserts nothing about it, and
    /// the retained page is the evidence for anyone who later wants to. What it must NOT do is
    /// refuse the delivery, because that would throw away the admitted rows beside it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void APropertyThisFamilyDoesNotAdmitIsRetainedRatherThanRefused()
    {
        const string NotAdmitted = "http://data.legilux.public.lu/resource/ontology/jolux#titleDraft";

        var result = Decode(
            Row(predicate: NotAdmitted, value: Literal("Projet de loi")),
            Row(predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Iri("http://data.legilux.public.lu/resource/authority/legal-status/EN-COURS")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Records!, "only the admitted predicate becomes a record.");

        // RETAINED WITH ITS SHAPE, not as a bare predicate name. Reducing it to a string would throw
        // away what makes it evidence: which draft, what value, what kind of term.
        var retained = result.RetainedNotAdmitted.Single();
        Assert.AreEqual(NotAdmitted, retained.PredicateIri);
        Assert.AreEqual(Draft, retained.DraftIri);
        Assert.AreEqual("Projet de loi", retained.Value);
        Assert.AreEqual("literal", retained.ValueKind);
        Assert.AreEqual(
            LuxembourgDraftRetentionReason.PredicateOutsideTheAcceptedVocabulary, retained.Reason);
        Assert.AreEqual(RunRefFor(1).ResourceId, retained.SourceObservationId);
        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri, result.Records![0].PredicateIri);
    }

    /// <summary>
    /// Every value shape the first broad delivery actually carried decodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED, NOT ENUMERATED FROM THE SPEC. These are the four (term, datatype column) pairs the
    /// ten-draft broad acquisition returned, in their measured proportions: 118 IRI values, 40 plain
    /// literals, 10 <c>xsd:anyURI</c> literals - which is what parliamentDraftUrl carries - and 10
    /// <c>xsd:dateTime</c> literals.
    /// </para>
    /// <para>
    /// The narrow five-predicate query returned IRIs and nothing else, so three of these four shapes
    /// had never reached this decoder. The plain-literal one refused a real page.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryValueShapeTheBroadDeliveryCarriedDecodes()
    {
        const string XsdStringIri = "http://www.w3.org/2001/XMLSchema#string";
        const string XsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime";
        var status = LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri;

        foreach (var (value, expectedDatatype, what) in new[]
                 {
                     (Iri(Directive), string.Empty, "an IRI value"),
                     (Literal("Projet de loi"), XsdStringIri, "a plain literal, serialised with no datatype"),
                     (Literal("https://www.chd.lu/fr/dossier/8357", XsdAnyUri), XsdAnyUri, "an xsd:anyURI literal"),
                     (Literal("2002-07-16T00:00:00", XsdDateTime), XsdDateTime, "an xsd:dateTime literal"),
                 })
        {
            var result = Decode(Row(predicate: status, value: value));

            Assert.AreEqual(
                LuxembourgDraftGraphProductionRefusal.None, result.Refusal,
                what + " must decode: " + result.Detail);
            Assert.AreEqual(expectedDatatype, result.Records![0].ValueDatatypeIri, what);
        }
    }

    /// <summary>A predicate that cannot be read at all still refuses the delivery.</summary>
    /// <remarks>
    /// The boundary the test above must not erode. "Not admitted" is a decision about vocabulary;
    /// a predicate term that is not a readable IRI is a statement about the DELIVERY, and admitting
    /// one would mean reading facts out of a response already known to be malformed.
    /// </remarks>
    [TestMethod]
    public void APredicateThatCannotBeReadStillRefusesTheDelivery()
    {
        var result = Decode(Row(predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
            value: Iri(Directive), key3: "not-the-predicate"));

        Assert.AreNotEqual(
            LuxembourgDraftGraphProductionRefusal.None, result.Refusal,
            "a row whose own keys contradict it is not admitted by the vocabulary check.");
    }

    /// <summary>A marker disagreeing with its term refuses the delivery whole.</summary>
    [TestMethod]
    public void AMarkerThatDisagreesWithItsTermRefusesTheDelivery()
    {
        var result = Decode(Row(value: Literal("x"), valueKind: "iri"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "value_kind");
    }

    /// <summary>
    /// A qualifier column contradicting the term it describes refuses the delivery.
    /// </summary>
    /// <remarks>
    /// Both columns are bound from <c>DATATYPE()</c> and <c>LANG()</c> over the very term they
    /// qualify, so an honest delivery cannot disagree. A row that does keys as one fact and decodes
    /// as another — the marker-versus-term rule applied to the qualifiers, which the sibling
    /// procedure-event producer needed a repair to learn.
    /// </remarks>
    [TestMethod]
    public void AQualifierColumnContradictingItsTermIsRefused()
    {
        var wrongDatatype = Decode(Row(
            value: Literal("2024-05-22", XsdDate), datatype: XsdAnyUri));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, wrongDatatype.Refusal);
        StringAssert.Contains(wrongDatatype.Detail!, "datatype_iri");

        var wrongLanguage = Decode(Row(
            value: Literal("en-cours", null, "fr"), language: "de"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, wrongLanguage.Refusal);
        StringAssert.Contains(wrongLanguage.Detail!, "language_tag");
    }

    /// <summary>
    /// Corrupting any one of the seven cursor keys refuses the row and names that key.
    /// </summary>
    /// <remarks>
    /// A sweep rather than seven near-identical tests, and driven off the live profile so a key
    /// added later is covered the day it appears. The honest value of several keys is a marker or an
    /// empty qualifier, and a producer that ignored one would agree with a fixture that also left it
    /// empty — two mutations survived the sibling family on exactly that.
    /// </remarks>
    [TestMethod]
    public void CorruptingAnySingleCursorKeyRefusesTheRowAndNamesIt()
    {
        var profile = Profile();
        var keys = profile.CursorVariables;

        Assert.HasCount(7, keys, "this family keys seven positions.");

        foreach (var key in keys)
        {
            var ordinal = profile.ProjectionVariables.ToList().IndexOf(key);
            Assert.IsGreaterThan(-1, ordinal, $"{key} is projected.");

            // A row rich enough that no key's honest value is empty by accident.
            var terms = Row(
                predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Literal("en-cours", null, "fr")).Terms.ToList();
            terms[ordinal] = Literal((terms[ordinal].Value ?? string.Empty) + "-not-delivered");

            var partitionKey = LuxembourgDraftGraphDiscoveryPlan.PartitionKeyFor([Draft]);
            var (cursorProof, boundRow) = Bound(
                partitionKey, [new RepeatedEnumerationRow(terms, terms, terms)]);

            var result = LuxembourgDraftGraphProducer.DecodeRows(
                boundRow, profile, cursorProof,
                LuxembourgDraftGraphBatchFactory.AssignBatches(InventoryOf(Draft))[0],
                partitionKey,
                "2026-09-10T13:50:31.0000000Z");

            Assert.AreEqual(
                LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal,
                $"{key} was corrupted and the row was still admitted, so that key keys nothing.");
            StringAssert.Contains(
                result.Detail!, key,
                $"the refusal must name {key} rather than another key that happened to differ.");
        }
    }

    /// <summary>The grouped count must be the term COUNT(*) delivers.</summary>
    [TestMethod]
    public void ACountCarryingTheWrongDatatypeIsRefused()
    {
        var result = Decode(Row(multiplicity: Literal("3", "http://www.w3.org/2001/XMLSchema#string")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "multiplicity");
    }

    /// <summary>
    /// Asking about a property this run never asked refuses rather than answering empty.
    /// </summary>
    [TestMethod]
    public void APropertyThisRunNeverAskedAboutIsRefusedRatherThanAnsweredEmpty()
    {
        var result = Decode(Row(), Row(draft: OtherDraft, key1: OtherDraft));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(2, result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.For("http://data.legilux.public.lu/resource/ontology/jolux#titleDraft"));
    }

    /// <summary>A refused production answers no question at all.</summary>
    [TestMethod]
    public void ARefusedProductionAnswersNothing()
    {
        var result = Decode(Row(value: Literal("x"), valueKind: "iri"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.IsNull(result.Records);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri));
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (plan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9102);
        return plan.BindCount(
            planResourceId,
            "urn:uuid:2b8d6f4a-5c73-4e91-a024-7f136bd95c82",
            "urn:uuid:3c9e705b-6d84-4fa2-b135-80247ce06d93",
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9102)).Request;
    }

    /// <summary>
    /// One page answering the draft for all five properties, in the publisher's own wire shape.
    /// </summary>
    private static string PageJson(IReadOnlyList<string> projection)
    {
        static object IriTerm(string value) => new Dictionary<string, string>
        {
            ["type"] = "uri",
            ["value"] = value,
        };

        static object LiteralTerm(string value, string? datatype = null, string? language = null)
        {
            var term = new Dictionary<string, string> { ["type"] = "literal", ["value"] = value };
            if (datatype is not null)
            {
                term["datatype"] = datatype;
            }

            if (language is not null)
            {
                term["xml:lang"] = language;
            }

            return term;
        }

        // ORDERED BY THE KEYSET, because that is what the page is ordered by and what the executor
        // advances on. The declaration order of the asked predicates is not their lexical order, and
        // a page delivered out of order refuses with CursorDidNotAdvance.
        var bindings = new List<Dictionary<string, object>>();
        foreach (var predicate in LuxembourgDraftGraphDiscoveryPlan.AskedAbout
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            var isIri = predicate == LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri
                || predicate == LuxembourgDraftGraphDiscoveryPlan.ResultingLegalResourcePredicateIri;
            var value = isIri ? Directive : "en-cours";

            // THE COLUMN AS THE ENGINE ANSWERS IT. A plain literal is serialised without a
            // datatype attribute, but DATATYPE() answers xsd:string - RDF 1.1 says a simple literal
            // IS an xsd:string. This fixture used to write an empty column here, which is a page
            // Legilux does not send, and the disagreement only surfaced when a broad delivery
            // carried forty real plain literals.
            var datatype = isIri ? string.Empty : "http://www.w3.org/2001/XMLSchema#string";

            bindings.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["draft"] = IriTerm(Draft),
                ["draft_kind"] = LiteralTerm("iri"),
                ["predicate"] = IriTerm(predicate),
                ["value"] = isIri ? IriTerm(value) : LiteralTerm(value),
                ["value_kind"] = LiteralTerm(isIri ? "iri" : "literal"),
                ["datatype_iri"] = LiteralTerm(datatype),
                ["language_tag"] = LiteralTerm(string.Empty),
                ["multiplicity"] = LiteralTerm("1", XsdInteger),
                ["key_1"] = LiteralTerm(Draft),
                ["key_2"] = LiteralTerm("iri"),
                ["key_3"] = LiteralTerm(predicate),
                ["key_4"] = LiteralTerm(value),
                ["key_5"] = LiteralTerm(isIri ? "iri" : "literal"),
                ["key_6"] = LiteralTerm(datatype),
                ["key_7"] = LiteralTerm(string.Empty),
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            head = new { link = Array.Empty<string>(), vars = projection },
            results = new { distinct = false, ordered = true, bindings = bindings.ToArray() },
        });
    }
}
