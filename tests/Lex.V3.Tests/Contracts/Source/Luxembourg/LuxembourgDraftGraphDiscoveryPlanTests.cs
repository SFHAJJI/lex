using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// What the draft-graph family asks the publisher, pinned at the query text it actually sends.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because the repair it guards had nothing guarding it. The candidate changed two
/// derived cursor keys in the page template and every test in the repository still passed, which
/// means the change back would also have passed. Every sibling plan --
/// <see cref="LuxembourgTranspositionIdentityDiscoveryPlan"/>,
/// <see cref="LuxembourgOpinionDiscoveryPlan"/>, the EU families -- pins its own template. This one
/// did not, and a producer test cannot stand in: the producer is fed authored pages, so the SPARQL
/// is never executed by anything the suite runs.
/// </para>
/// <para>
/// The assertions here are about the SHAPE OF THE QUESTION rather than its spelling. A reformatted
/// template should not fail; a template that stops sweeping the class, stops asking about a
/// property, drops the absence branch, or un-totalises a cursor key should.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphDiscoveryPlanTests
{
    /// <summary>
    /// The eager-guard form the EU guard bans repository wide, matched on the BIND that carries it
    /// so this file's own prose does not match itself.
    /// </summary>
    private const string EagerGuardForm = "BIND(IF(BOUND(";

    /// <summary>
    /// One draft the publisher actually holds, from the retained inventory run.
    /// </summary>
    /// <remarks>
    /// An invented IRI would bind and render exactly as well, and would make every assertion here
    /// about a subject Legilux has never heard of.
    /// </remarks>
    private const string InventoryDraft = "http://data.legilux.public.lu/eli/dl/pc/2002/215";

    [TestMethod]
    public void TheFamilySweepsTheClassAndAsksEveryDraftAboutEveryProperty()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();

        Assert.AreEqual(LuxembourgQueryPlan.PublisherEndpoint, plan.PublisherEndpoint);

        var sweep = "?draft a <" + LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri + "> .";
        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            // Class-scoped, not a caller's selection: the question is "every InitialDraft", so a
            // draft the caller had never heard of is still swept.
            StringAssert.Contains(
                template, sweep, "the family sweeps the class rather than a supplied list of drafts.");
            // NO PREDICATE FILTER, AND NO ASKED PREDICATE NAMED IN THE QUERY AT ALL. Measured:
            // with VALUES ?predicate in front of the triple, Legilux returned zero
            // parliamentDraftUrl rows for fifty drafts that carry them, and each dropped pair was
            // minted as a derived absence. The publisher is asked for everything it holds about
            // these subjects; admission to the five happens in the producer, where it is visible.
            Assert.IsFalse(
                template.Contains("VALUES ?predicate", StringComparison.Ordinal),
                "a publisher-side predicate filter was measured dropping rows the publisher holds.");

            foreach (var predicate in LuxembourgDraftGraphDiscoveryPlan.AskedAbout)
            {
                Assert.IsFalse(
                    template.Contains("<" + predicate + ">", StringComparison.Ordinal),
                    "an accepted predicate is an admission rule, not a question put to the publisher.");
            }

            // The count and the page must ask the SAME question, or the count proves nothing about
            // the rows it is compared against.
            Assert.AreEqual(
                1,
                template.Split(sweep, StringSplitOptions.None).Length - 1,
                "each template wraps exactly one instance of the row question.");
        }

        Assert.IsFalse(
            plan.PageTemplate.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
            "pagination is by keyset; OFFSET over an unstable order would skip and repeat rows.");

        // THE CLASS MEMBERSHIP TRIPLE SURVIVES THE BATCH. A batch that replaced `?draft a
        // <InitialDraft>` with a bare VALUES list would answer for anything a caller named,
        // including something that is not a draft at all, and the sweep would stop being a sweep of
        // the class. Both are asked: the batch bounds the request, the triple keeps it about drafts.
        StringAssert.Contains(plan.PageTemplate, "VALUES ?draft {");
        StringAssert.Contains(plan.PageTemplate, "SELECT DISTINCT ?draft WHERE {");
    }

    /// <summary>
    /// The five are closed, ordered and distinct, and <c>draftTransposes</c> is one of them.
    /// </summary>
    /// <remarks>
    /// The accepted E8 provisions name InitialDraft, OpinionConseilEtat and draftTransposes
    /// together, and 24-research-users carries draftTransposes as a property OF InitialDraft rather
    /// than a family of its own. So a draft-graph family that swept the class while declining to ask
    /// about transposition would satisfy a reader counting families and answer none of the question.
    /// </remarks>
    [TestMethod]
    public void TheAskedPropertiesAreClosedOrderedAndIncludeTheTranspositionIntention()
    {
        var asked = LuxembourgDraftGraphDiscoveryPlan.AskedAbout;

        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                LuxembourgDraftGraphDiscoveryPlan.ParliamentDraftUrlPredicateIri,
                LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri,
                LuxembourgDraftGraphDiscoveryPlan.ResultingLegalResourcePredicateIri,
                LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri,
            },
            asked.ToArray(),
            "the order is part of the canonical identity, so it is part of what a reviewer compares.");
        Assert.HasCount(5, asked.Distinct(StringComparer.Ordinal).ToArray());
        StringAssert.EndsWith(
            LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri,
            "draftTransposes",
            "the draft-to-directive intention is asked about by this family, not deferred.");
    }

    /// <summary>
    /// The publisher is asked for present facts only, with a mandatory value triple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THREE SHAPES THAT ASKED THE PUBLISHER FOR THE ABSENT CASE ARE ALL FORBIDDEN HERE, each
    /// because it was measured failing. A <c>FILTER NOT EXISTS</c> inside a <c>UNION</c> branch was
    /// permanently inert: a branch binds nothing from outside itself, so NOT EXISTS over three
    /// unbound terms asked whether ANY triple exists, which is always true. An <c>OPTIONAL</c>
    /// emitting a row per pair timed the publisher out at ~49 seconds, ordered and unordered alike.
    /// Five constant-predicate UNION branches timed out the same way.
    /// </para>
    /// <para>
    /// The gap did not disappear with them. It moved to where it can be stated honestly: a derived
    /// absence over the requested batch, in <c>LuxembourgDraftPropertyCoverage</c>. S2-A03 requires
    /// gaps to be first class; it does not require the publisher to utter them, and a row claiming
    /// to be one would be our inference in the publisher's voice.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ThePublisherIsAskedForPresentFactsWithAMandatoryValueTriple()
    {
        var page = LuxembourgDraftGraphDiscoveryPlan.Create().PageTemplate;

        Assert.IsFalse(
            page.Contains("FILTER NOT EXISTS", StringComparison.Ordinal),
            "a NOT EXISTS over terms its own branch does not bind is inert.");
        Assert.IsFalse(
            page.Contains("UNION", StringComparison.Ordinal),
            "the five constant-predicate branches timed out and are not coming back.");
        Assert.IsFalse(
            page.Contains("OPTIONAL", StringComparison.Ordinal),
            "an OPTIONAL emitting a row per pair is what the publisher would not serve.");

        // THE PAIR IS STILL BOUND BEFORE ITS VALUE IS SOUGHT: the cross product comes from the class
        // triple and the predicate VALUES, and the mandatory triple then keeps only the pairs that
        // carry a value. Asserted by order rather than by indentation - a reformat is not a defect.
        var classTriple = page.IndexOf(
            "?draft a <" + LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri + ">",
            StringComparison.Ordinal);
        var valueTriple = page.IndexOf("?draft ?predicate ?value .", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, classTriple);
        Assert.IsGreaterThanOrEqualTo(0, valueTriple);
        Assert.IsTrue(
            classTriple < valueTriple,
            "the subject is constrained to the class before its triples are matched.");
        Assert.IsFalse(
            page.Contains("VALUES ?predicate", StringComparison.Ordinal),
            "the predicate is free: a publisher-side filter was measured dropping held rows.");

        // AND NO MARKER MAY SAY "unbound". Under a mandatory triple no solution can leave the value
        // unbound, so a page able to emit that marker could only do so by fabricating it - which is
        // exactly the publisher-returned absence this design exists to never produce.
        Assert.IsFalse(
            page.Contains("\"" + LuxembourgDraftGraphDiscoveryPlan.UnboundKind + "\"", StringComparison.Ordinal),
            "the query may not mint an unbound marker it cannot honestly observe.");

        // ?value itself is deliberately NOT totalised. Its absence IS the unbound fact, and a page
        // that bound it to "" would destroy the fact while appearing to succeed.
        Assert.IsFalse(
            page.Contains("BIND(\"\" AS ?value)", StringComparison.Ordinal),
            "the absent value must stay absent; only the cursor key derived from it becomes total.");
    }

    /// <summary>
    /// Every derived cursor key is total, against an engine that leaves the source variable unbound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO SEPARATE MEASURED CAUSES, one symptom. <c>key_4</c> answers the eager-IF defect recorded
    /// on <see cref="EuObjectFactsDiscoveryPlan"/>: this engine selects IF's branch correctly and
    /// evaluates the arguments EAGERLY, so <c>STR</c> on the unbound term raised, the erroring BIND
    /// left the key unbound, and SPARQL's JSON omitted it from 8 of 41 rows.
    /// </para>
    /// <para>
    /// <c>key_6</c> and <c>key_7</c> answer a different one. The retained page
    /// <c>expressionfacts-langtag-unbound-datatype.bin</c> shows this engine declining to answer
    /// <c>DATATYPE()</c> with <c>rdf:langString</c> for a language-tagged literal: that BIND errors
    /// and <c>datatype_iri</c> is omitted from the binding, taking any key derived from it along.
    /// A language-tagged <c>statusDraft</c> value is the ordinary case here, not the exotic one.
    /// </para>
    /// <para>
    /// So the KEYS are totalised and the COLUMNS are not, which is the division the EU families
    /// already settled on. What the publisher answers is recorded as the publisher answers it; what
    /// this design derives for its own cursor is made total, because a keyset short a component
    /// cannot order rows.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryDerivedCursorKeyIsTotalisedAgainstAnEngineThatLeavesItsSourceUnbound()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();

        StringAssert.Contains(plan.PageTemplate, "BIND(COALESCE(STR(?value), \"\") AS ?key_4)");
        StringAssert.Contains(plan.PageTemplate, "BIND(COALESCE(?datatype_iri, \"\") AS ?key_6)");
        StringAssert.Contains(plan.PageTemplate, "BIND(COALESCE(?language_tag, \"\") AS ?key_7)");

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            Assert.IsFalse(
                template.Contains(EagerGuardForm, StringComparison.Ordinal),
                "this guard reads as present and does nothing on this publisher's engine.");
        }
    }

    /// <summary>
    /// The qualifier columns stay exactly as the publisher answers them, absence included.
    /// </summary>
    /// <remarks>
    /// The pairing with the test above is the whole point and is easy to undo by accident. Making
    /// these columns total too would look like more safety and would delete a fact: the decoder
    /// distinguishes a literal whose datatype the engine WOULD NOT GIVE from one it answered as
    /// empty, and it can only do that while the column is allowed to be missing. The EU families
    /// left the same binds alone for the same measured reason, and their retained page settles the
    /// adjacent worry: of its 41 bindings 23 are IRI-valued and <c>datatype_iri</c> is present and
    /// empty in every one, so this engine does not raise inside DATATYPE or LANG on a bound IRI.
    /// </remarks>
    [TestMethod]
    public void OnlyTheValueDerivedColumnsAreTotalisedAndTheDraftMarkerIsNot()
    {
        var page = LuxembourgDraftGraphDiscoveryPlan.Create().PageTemplate;

        // NO VALUE COLUMN IS TOTALISED, AND THAT IS MEASURED ON THIS PUBLISHER. The eager-IF raise
        // these COALESCEs once guarded came from dereferencing an UNBOUND variable; the mandatory
        // triple removes that cause. Applying DATATYPE or LANG to a BOUND term of the wrong type is
        // a different case, and Legilux does not raise on it: the retained 103-row delivery was
        // produced by exactly these un-COALESCEd BINDs and carried datatype_iri and language_tag in
        // every one of its rows, all IRI-valued.
        //
        // Totalising them would not be harmless caution, it would DESTROY a fact. This engine will
        // not answer DATATYPE() with rdf:langString, so on a language-tagged literal that BIND
        // errors and the column drops - the one measured absence the producer admits, and what lets
        // it tell a language-tagged literal from a plain one. COALESCE would swallow that into ""
        // and erase the distinction #532 was repaired to preserve.
        foreach (var derived in new[] { "?value_kind", "?datatype_iri", "?language_tag" })
        {
            // THE COLUMN'S OWN BIND, not "the page contains a COALESCE somewhere". An earlier
            // version of this loop asserted the latter, and a mutation touching one column alone
            // survived it because another column's COALESCE satisfied every iteration.
            Assert.IsFalse(
                BindExpressionFor(page, derived).Contains("COALESCE(", StringComparison.Ordinal),
                $"{derived} reads a value the mandatory triple always binds, so it is not totalised.");
        }

        // THE KEYS STILL ARE, and for a reason that survives: a langString row can leave
        // ?datatype_iri unbound, and a keyset short a component cannot order rows.
        foreach (var key in new[] { "?key_6", "?key_7" })
        {
            StringAssert.Contains(
                BindExpressionFor(page, key), "COALESCE(",
                $"{key} must stay total even when the column it reads is absent.");
        }

        // AND NOT ONE COLUMN MORE. ?draft is bound by the batch and the class triple on every row,
        // so totalising its marker would be covering for a case that cannot arise - and a guard for
        // an impossible case is a guard nobody can ever see fail.
        StringAssert.Contains(
            page,
            "BIND(IF(isIRI(?draft), \"iri\", \"unsupported_blank_node\") AS ?draft_kind)",
            "the draft marker needs no totalisation and does not get it.");

        // The qualifier pair still distinguishes what it always did: a language-tagged literal is
        // the one with a non-empty language beside a literal kind, whatever its datatype reads.
        StringAssert.Contains(page, "IF(isLiteral(?value), STR(DATATYPE(?value)), \"\")");
        StringAssert.Contains(page, "IF(isLiteral(?value), LANG(?value), \"\")");
    }

    /// <summary>
    /// The query AS RENDERED carries the repair, not merely the template it was rendered from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other assertion here reads <c>PageTemplate</c>. That is the text before its slots are
    /// filled, and the defect this repairs was invisible in the template too — the inert UNION read
    /// perfectly well. What reaches the publisher is the rendered body, so this asserts on that: the
    /// batch members substituted as real IRI terms, the pair bound before the OPTIONAL, and no slot
    /// left behind.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheRenderedQueryCarriesTheBoundPairAndItsMandatoryValue()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var bound = plan.BindPage(
            LuxembourgQueryPass.Pass1,
            [InventoryDraft],
            null,
            0,
            new SourceArtifactRef(
                "urn:uuid:4c81b0d7-95e2-4f31-8a67-2e0d5b93a4f1", new string('7', 64)),
            "urn:uuid:0a6f3e28-71b4-4d95-9c30-8f52e1b7d6a4",
            "urn:uuid:6b24e0f7-3a19-4c86-b502-9d7f4e6a1c38",
            RendererSource());

        var body = System.Text.Encoding.UTF8.GetString(bound.Request.CopyRequestBody());
        StringAssert.StartsWith(body, "query=");
        var query = Uri.UnescapeDataString(body["query=".Length..].Replace('+', ' '));

        // No slot survives rendering. A leftover would be sent to the publisher verbatim.
        Assert.IsFalse(query.Contains("{batch_draft_", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains(":iri}", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains(":uint}", StringComparison.Ordinal));

        // The member arrives as a real IRI term, and the pair is bound before its value is sought.
        StringAssert.Contains(query, "<" + InventoryDraft + ">");
        var classTriple = query.IndexOf("?draft a <", StringComparison.Ordinal);
        var valueTriple = query.IndexOf("?draft ?predicate ?value .", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, classTriple);
        Assert.IsGreaterThanOrEqualTo(0, valueTriple);
        Assert.IsTrue(classTriple < valueTriple);
        Assert.IsFalse(query.Contains("VALUES ?predicate", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("FILTER NOT EXISTS", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("OPTIONAL", StringComparison.Ordinal));
    }

    /// <summary>
    /// The keyset separates two rows that share a lexical form, and orders by all of it.
    /// </summary>
    [TestMethod]
    public void TheKeysetIsInjectiveOverTheGroupedRowAndOrdersByAllOfIt()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7" },
            profile.CanonicalKeyVariables.ToArray(),
            "draft, draft kind, predicate, value, value kind, datatype and language: a draft with two "
            + "transposition targets needs the value to advance, and two literals sharing a lexical "
            + "form need the qualifiers.");
        CollectionAssert.AreEqual(profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.Select(static key => "last_" + key).ToArray(),
            profile.CursorParameterNames.ToArray());

        StringAssert.Contains(
            plan.PageTemplate,
            "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7",
            "the order must cover the whole keyset or the cursor cannot be strictly increasing.");
    }

    [TestMethod]
    public void TheDeliveryProfilePinsThePublisherCoordinatesAndItsBoundSelection()
    {
        var profile = LuxembourgDraftGraphDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        Assert.AreEqual("application/sparql-results+json", profile.ExpectedMediaType);
        Assert.AreEqual(RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
        Assert.AreEqual("count", profile.CountVariable);
        Assert.AreEqual("pass_id", profile.PassParameterName);
        Assert.AreEqual("has_cursor", profile.HasCursorParameterName);
        CollectionAssert.AreEqual(
            new[]
            {
                "draft", "draft_kind", "predicate", "value", "value_kind", "datatype_iri",
                "language_tag", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7",
            },
            profile.ProjectionVariables.ToArray());

        // THIS IS THE LATER SLICE THE EMPTY-SELECTION COMMENT ANTICIPATED. The selection is no
        // longer empty, and it binds BEFORE pass_id because RequireInputRoleShape compares
        // SelectionParameterNames.Append(PassParameterName) by sequence.
        //
        // The class is not narrowed by it. Legilux refused this family's query unbounded, twice,
        // with SR319; the batch is a PARTITION of the class whose members come from a proven
        // inventory of the whole of it, which is what keeps "every InitialDraft" true of a run that
        // asks in fifty-draft pieces.
        Assert.HasCount(
            LuxembourgDraftGraphDiscoveryPlan.BatchCapacity, profile.SelectionParameterNames);
        Assert.AreEqual("batch_draft_000", profile.SelectionParameterNames[0]);
        Assert.AreEqual("batch_draft_049", profile.SelectionParameterNames[^1]);

        // The arithmetic the capacity rests on, asserted rather than restated in a comment: every
        // member is its own parameter, and the pass, the cursor flag and the seven continuation
        // keys have to fit beside them under MachineQueryValidation's ceiling of 64.
        Assert.IsLessThanOrEqualTo(
            64,
            profile.SelectionParameterNames.Count + 2 + profile.CanonicalKeyVariables.Count,
            "a batch that could not be bound must not be mintable.");
    }

    /// <summary>
    /// The two passes ask the same question with different page sizes, which is what makes the
    /// repeated enumeration a comparison rather than a repetition.
    /// </summary>
    [TestMethod]
    public void TheTwoPassesDifferInPageSizeAndNotInQuestion()
    {
        // Through PageLimit rather than the two constants, so this also proves the lookup is total
        // over the pass enum: a third pass added without a limit throws here rather than at the
        // publisher.
        var limits = Enum.GetValues<LuxembourgQueryPass>()
            .Select(LuxembourgDraftGraphDiscoveryPlan.PageLimit)
            .ToArray();

        Assert.HasCount(
            limits.Distinct().Count(),
            limits,
            "identical limits would let a page-boundary defect reproduce itself and agree.");
        Assert.AreEqual(
            1,
            LuxembourgDraftGraphDiscoveryPlan.Create().PageTemplate
                .Split("LIMIT {page_limit:uint}", StringSplitOptions.None).Length - 1,
            "the limit is a bound parameter, so both passes send the same text.");
    }

    /// <summary>
    /// The canonical identity covers the question, so widening what is asked changes the digest.
    /// </summary>
    [TestMethod]
    public void ThePlanIdentityCoversTheQuestionItAsks()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());

        StringAssert.Contains(identity, plan.CountTemplate.Trim());
        StringAssert.Contains(identity, plan.PageTemplate.Trim());
        StringAssert.Contains(identity, LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri);
        foreach (var predicate in LuxembourgDraftGraphDiscoveryPlan.AskedAbout)
        {
            StringAssert.Contains(identity, predicate);
        }

        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(plan.CopyCanonicalIdentityBytes())),
            plan.ArtifactRef.Sha256,
            "the reference must digest the identity it claims to name.");
    }

    [TestMethod]
    public void BindingProducesClosedCountAndPageRequests()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();
        var source = RendererSource();

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            [InventoryDraft],
            "urn:uuid:9c2f4d61-77a3-4a2e-8f0b-1d5e6a7c8b90",
            "urn:uuid:5e8b1c30-42d7-4f19-b6a4-0c3d9e2f7a15",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
            [InventoryDraft],
            null,
            0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:1f7a6c92-3d40-4b8e-95c1-7e2b0a4d6f38",
            "urn:uuid:8d40e2b7-51c6-4a03-9e7f-2c1b5a8d3406",
            source);

        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.PartitionMemberKey,
            count.InputArtifact.PartitionBinding.MemberKey);
        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.PartitionMemberKey,
            page.InputArtifact.PartitionBinding.MemberKey);

        // The batch binds FIRST and at full capacity whatever its real size, so the input role is
        // the same for every batch including a short final one. Then the pass; then, on a page,
        // has_cursor; then one parameter per key on a continuation, so an empty cursor is a first
        // page rather than a short keyset.
        var capacity = LuxembourgDraftGraphDiscoveryPlan.BatchCapacity;
        Assert.HasCount(capacity + 1, count.InputArtifact.OrderedParameters);
        Assert.AreEqual("batch_draft_000", count.InputArtifact.OrderedParameters[0].Name);
        Assert.AreEqual("pass_id", count.InputArtifact.OrderedParameters[capacity].Name);
        Assert.HasCount(capacity + 2, page.InputArtifact.OrderedParameters);
        Assert.AreEqual("has_cursor", page.InputArtifact.OrderedParameters[capacity + 1].Name);
    }

    /// <summary>
    /// A continuation cursor is all seven keys or it is refused.
    /// </summary>
    /// <remarks>
    /// A short cursor would render a page whose keyset filter compared fewer components than the
    /// order, which does not fail loudly: it silently re-delivers or skips rows at the boundary.
    /// </remarks>
    [TestMethod]
    public void APartialContinuationCursorIsRefusedRatherThanRendered()
    {
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            LuxembourgQueryPass.Pass1,
            [InventoryDraft],
            ["a", "b", "c"],
            0,
            new SourceArtifactRef(
                "urn:uuid:0b6d4e19-8c37-4f52-a1d0-6e9f3b2c7a48",
                new string('0', 64)),
            "urn:uuid:6a3c8e50-9b21-4d7f-83e6-4f0a1c5d92b7",
            "urn:uuid:2c9f5b18-7e43-4a60-b2d9-8a1e6c40f375",
            RendererSource()));
    }

    /// <summary>
    /// The one BIND expression that assigns <paramref name="variable"/>, and nothing else.
    /// </summary>
    /// <remarks>
    /// Asserting against the whole template lets one column's property stand in for another's,
    /// which is how a mutation removing totalisation from a single column survived. This walks back
    /// from the assignment to the BIND that owns it, so each column is judged on its own text.
    /// </remarks>
    private static string BindExpressionFor(string template, string variable)
    {
        var assignment = template.IndexOf(") AS " + variable + ")", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, assignment, $"{variable} is bound by nothing.");
        var open = template.LastIndexOf("BIND(", assignment, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, open, $"{variable} has no BIND of its own.");
        return template[open..(assignment + 5 + variable.Length)];
    }

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-initial-draft-graph-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:3a1d0f77-6c58-4f6a-9a1e-2b6f0c4d8e13",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }
}
