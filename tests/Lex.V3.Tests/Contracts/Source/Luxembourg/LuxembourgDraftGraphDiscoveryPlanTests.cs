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

        StringAssert.Contains(page, "FILTER NOT EXISTS { ?draft ?predicate ?missing_value }");
        StringAssert.Contains(
            page,
            "BIND(\"" + LuxembourgDraftGraphDiscoveryPlan.UnboundKind + "\" AS ?value_kind)",
            "the absence carries its own marker rather than arriving as a missing row.");
        Assert.AreEqual(
            1,
            page.Split("UNION", StringSplitOptions.None).Length - 1,
            "exactly two arms: the property answered, and the property answered as absent.");

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
    public void TheQualifierColumnsAreLeftAsThePublisherAnswersThem()
    {
        var page = LuxembourgDraftGraphDiscoveryPlan.Create().PageTemplate;

        StringAssert.Contains(page, "BIND(IF(isLiteral(?value), STR(DATATYPE(?value)), \"\") AS ?datatype_iri)");
        StringAssert.Contains(page, "BIND(IF(isLiteral(?value), LANG(?value), \"\") AS ?language_tag)");
        Assert.IsFalse(
            page.Contains("BIND(COALESCE(STR(DATATYPE(", StringComparison.Ordinal),
            "totalising the column would erase the difference between a datatype withheld and one answered empty.");
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
    public void TheDeliveryProfilePinsThePublisherCoordinatesAndAsksForNoSelection()
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

        // EMPTY, and that is a decision rather than an omission: the scope is a class and the asked
        // properties are fixed by the plan, so there is nothing for a caller to select. Written down
        // because RequireInputRoleShape compares SelectionParameterNames.Append(PassParameterName)
        // by sequence, so a later slice that adds a selection binds it BEFORE pass_id.
        Assert.IsEmpty(profile.SelectionParameterNames);
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
            "urn:uuid:9c2f4d61-77a3-4a2e-8f0b-1d5e6a7c8b90",
            "urn:uuid:5e8b1c30-42d7-4f19-b6a4-0c3d9e2f7a15",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2,
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

        // The count asks only which pass. The page adds has_cursor, and a continuation adds one
        // parameter per key -- so an empty cursor is a first page rather than a short keyset.
        Assert.HasCount(1, count.InputArtifact.OrderedParameters);
        Assert.AreEqual("pass_id", count.InputArtifact.OrderedParameters[0].Name);
        Assert.HasCount(2, page.InputArtifact.OrderedParameters);
        Assert.AreEqual("has_cursor", page.InputArtifact.OrderedParameters[1].Name);
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
