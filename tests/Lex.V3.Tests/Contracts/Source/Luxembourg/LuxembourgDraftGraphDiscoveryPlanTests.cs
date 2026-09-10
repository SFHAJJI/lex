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
            StringAssert.Contains(template, "VALUES ?predicate {");

            foreach (var predicate in LuxembourgDraftGraphDiscoveryPlan.AskedAbout)
            {
                StringAssert.Contains(
                    template,
                    "<" + predicate + ">",
                    "every asked property must appear in the query that claims to ask about it.");
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
    /// A property the publisher holds nothing for delivers a row saying so.
    /// </summary>
    /// <remarks>
    /// This is the family's reason to exist. Without the absence branch a draft holding no
    /// transposition intention and a draft the query failed to reach are the same empty result, and
    /// S2-A03 requires gaps to be first class rather than false absences.
    /// </remarks>
    [TestMethod]
    public void APropertyThePublisherHoldsNothingForStillDeliversARow()
    {
        var page = LuxembourgDraftGraphDiscoveryPlan.Create().PageTemplate;

        // THE INERT SHAPE MUST NOT COME BACK. A UNION is evaluated on its own and then joined, so
        // inside its branch neither ?draft nor ?predicate was bound and NOT EXISTS over three
        // unbound terms asked whether ANY triple exists - always true, so the branch never fired.
        // The first bounded batch ever sent to Legilux returned 103 rows for 50 drafts with not one
        // unbound marker among them.
        Assert.IsFalse(
            page.Contains("FILTER NOT EXISTS", StringComparison.Ordinal),
            "the absence branch may not be a NOT EXISTS over terms the branch does not bind.");
        Assert.IsFalse(
            page.Contains("UNION", StringComparison.Ordinal),
            "the pair is bound once and its value observed, rather than two arms being unioned.");

        // THE PAIR IS BOUND BEFORE ITS VALUE IS LOOKED FOR. That ordering is the whole repair: the
        // cross product comes from the class triple and the predicate VALUES, and OPTIONAL then
        // observes each pair's value, so a pair the publisher holds nothing for still delivers a row.
        var classTriple = page.IndexOf(
            "?draft a <" + LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri + ">",
            StringComparison.Ordinal);
        var predicateValues = page.IndexOf("VALUES ?predicate {", StringComparison.Ordinal);
        var optional = page.IndexOf("OPTIONAL {", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, classTriple);
        Assert.IsTrue(
            classTriple < predicateValues && predicateValues < optional,
            "the draft and the predicate must both be bound before the OPTIONAL observes a value.");
        // The triple itself, without pinning indentation: a reformat is not a defect.
        StringAssert.Contains(page, "?draft ?predicate ?value .");
        Assert.IsTrue(
            optional < page.IndexOf("?draft ?predicate ?value .", StringComparison.Ordinal),
            "the value triple sits inside the OPTIONAL, which is what makes an absent pair a row.");

        // And the unbound row still says it is unbound, which is now carried by the totalised marker
        // rather than by a branch that never ran.
        StringAssert.Contains(
            page,
            "\"" + LuxembourgDraftGraphDiscoveryPlan.UnboundKind + "\") AS ?value_kind)",
            "a pair with no value delivers a row whose marker says so.");

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

        // REQUIRED BY THE OPTIONAL SHAPE, not preferred. OPTIONAL leaves ?value unbound for a pair
        // the publisher holds nothing for, and this engine evaluates IF's arguments EAGERLY, so
        // every BIND dereferencing it raises on exactly the rows the absence case exists to deliver
        // and the erroring BIND drops its variable from the binding. Without COALESCE the unbound
        // rows would arrive missing the marker that says they are unbound.
        foreach (var derived in new[] { "?value_kind", "?datatype_iri", "?language_tag" })
        {
            StringAssert.Contains(
                page,
                "BIND(COALESCE(",
                $"{derived} reads a value the OPTIONAL may leave unbound.");
            StringAssert.Contains(page, ") AS " + derived + ")");
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
    public void TheRenderedQueryCarriesTheBoundPairAndItsOptionalValue()
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
        var predicateValues = query.IndexOf("VALUES ?predicate {", StringComparison.Ordinal);
        var optional = query.IndexOf("OPTIONAL {", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, classTriple);
        Assert.IsTrue(classTriple < predicateValues && predicateValues < optional);
        Assert.IsFalse(query.Contains("FILTER NOT EXISTS", StringComparison.Ordinal));
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
