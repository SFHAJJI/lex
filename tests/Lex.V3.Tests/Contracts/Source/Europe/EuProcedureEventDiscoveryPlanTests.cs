using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E8's live half for procedure events, slice one: the bounded Cellar family that asks
/// which events belong to a batch of dossiers.
/// </summary>
/// <remarks>
/// The publisher terms below are written out as independent literals rather than read from the
/// vocabulary the plan is built from. Comparing the plan against its own constants would be a
/// comparison between a value and itself, which is a defect this repository has already had to fix
/// once in the E6 test file.
/// </remarks>
[TestClass]
public sealed class EuProcedureEventDiscoveryPlanTests
{
    private const string PartOfDossier =
        "http://publications.europa.eu/ontology/cdm#event_legal_part_of_dossier";
    private const string EventDate = "http://publications.europa.eu/ontology/cdm#event_legal_date";

    private static readonly string[] OneDossier =
    [
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
    ];

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-procedure-event-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:7b3e1c92-4d68-4a05-9f27-8c41b6de035a",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    private static string SentBody(EuProcedureEventBoundQuery bound) =>
        Encoding.UTF8.GetString(bound.Request.CopyRequestBody());

    /// <summary>
    /// The query names only the two publisher terms it needs, and asks for no event text.
    /// </summary>
    /// <remarks>
    /// Pinned as an exact census of the terms the template names rather than as a search for words
    /// somebody thought of. A blocklist refuses only the shapes it anticipates; pinning the whole set
    /// refuses everything nobody anticipated too. <c>?event a ?event_type</c> deliberately names no
    /// class IRI, because which types an event declares is the publisher's answer rather than this
    /// plan's question.
    /// </remarks>
    [TestMethod]
    public void TheQueryNamesOnlyTheTwoPublisherTermsItNeeds()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            var iris = System.Text.RegularExpressions.Regex
                .Matches(template, "<(?<iri>[^<>{}]+)>")
                .Select(match => match.Groups["iri"].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { PartOfDossier, EventDate }.Order(StringComparer.Ordinal).ToArray(),
                iris,
                "the query names the edge it walks and the date it reads, and nothing else.");
        }
    }

    /// <summary>
    /// Every declared type arrives as its own row rather than one concatenated string.
    /// </summary>
    /// <remarks>
    /// <see cref="EuProcedureEventObservation"/> retains every type and refuses the observation when
    /// any single one is not an IRI. That refusal is only meaningful if each term reaches the decoder
    /// as a term, so a <c>GROUP_CONCAT</c> here would quietly make the contract's own guard
    /// unreachable from a real delivery.
    /// </remarks>
    [TestMethod]
    public void EveryDeclaredTypeArrivesAsItsOwnRowRatherThanAConcatenation()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, "?event a ?event_type .");
            Assert.IsFalse(
                template.Contains("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase),
                "concatenating the types would destroy the per-term authority the contract keeps.");
            StringAssert.Contains(
                template, "GROUP BY ?event ?event_kind ?dossier ?event_type ?type_kind",
                "the type is grouped on, so one event contributes one row per declared type.");
        }
    }

    /// <summary>
    /// A missing date is asked for explicitly, and its branch pins the triple rather than the word.
    /// </summary>
    /// <remarks>
    /// A guard asserting only that the predicate appears somewhere in the template is satisfied by
    /// the <c>FILTER NOT EXISTS</c> branch alone — that exact weaker guard was shipped on the E6 plan
    /// and found in review. Both branches are pinned as whole triples here.
    /// </remarks>
    [TestMethod]
    public void TheMissingDateBranchIsAskedForAndPinsItsOwnTriple()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, $"?event <{EventDate}> ?event_date .");
            StringAssert.Contains(
                template, $"FILTER NOT EXISTS {{ ?event <{EventDate}> ?missing_date }}");
            StringAssert.Contains(template, "BIND(\"unbound\" AS ?date_kind)");
        }
    }

    /// <summary>
    /// The batch is deduplicated before it reaches the join.
    /// </summary>
    /// <remarks>
    /// SPARQL solution mappings are a multiset: a padded <c>VALUES</c> block listing one dossier
    /// fifty times joins fifty times and <c>COUNT(*)</c> counts every one. This seat asserted the
    /// opposite once — in a docstring, a commit message and a test comment at the same time — and was
    /// shown the measurement. The subquery is what makes padding a padding.
    /// </remarks>
    [TestMethod]
    public void ThePaddedBatchIsDeduplicatedBeforeItReachesTheJoin()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(template, "SELECT DISTINCT ?dossier WHERE {");
        }

        // And it survives into the bytes actually sent, not only the template.
        var count = plan.BindCount(
            EuProcedureEventQueryPass.Pass1, OneDossier,
            "urn:uuid:1a2b3c4d-5e6f-4708-8901-abcdef012345",
            "urn:uuid:2b3c4d5e-6f70-4819-8a12-bcdef0123456",
            Source());
        StringAssert.Contains(SentBody(count), "SELECT DISTINCT ?dossier");
    }

    /// <summary>
    /// The bound input names the selection first and <c>pass_id</c> last.
    /// </summary>
    /// <remarks>
    /// <c>RepeatedEnumerationDeliveryProof.RequireInputRoleShape</c> builds its expectation as
    /// <c>SelectionParameterNames.Append(PassParameterName)</c> and compares the ordered roles by
    /// sequence. The case-law family bound <c>pass_id</c> ahead of its fifty selection slots and could
    /// therefore refuse correctly while never once succeeding. Asserted against the profile's own
    /// names rather than a transcribed list, so a renamed selection cannot agree with a stale copy.
    /// </remarks>
    [TestMethod]
    public void TheBoundInputOrdersTheSelectionBeforeThePassIdentifier()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        var count = plan.BindCount(
            EuProcedureEventQueryPass.Pass1, OneDossier,
            "urn:uuid:3c4d5e6f-7081-492a-8b23-cdef01234567",
            "urn:uuid:4d5e6f70-8192-4a3b-8c34-def012345678",
            Source());

        CollectionAssert.AreEqual(
            profile.SelectionParameterNames.Append(profile.PassParameterName).ToArray(),
            count.InputArtifact.OrderedParameters.Select(static parameter => parameter.Name).ToArray(),
            "this is the exact expectation RequireInputRoleShape builds and compares by sequence.");
    }

    /// <summary>The delivery profile pins the whole row and the nine-part keyset.</summary>
    [TestMethod]
    public void TheDeliveryProfilePinsTheWholeRowAndItsKeyset()
    {
        var profile = EuProcedureEventDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso, profile.Dialect);
        CollectionAssert.AreEqual(
            new[]
            {
                "event", "event_kind", "dossier", "event_type", "type_kind",
                "event_date", "date_kind", "date_datatype", "date_language", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7", "key_8", "key_9",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6", "key_7", "key_8", "key_9" },
            profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        Assert.HasCount(50, profile.SelectionParameterNames);
        Assert.AreEqual("requested_dossier_01", profile.SelectionParameterNames[0]);
        Assert.AreEqual("requested_dossier_50", profile.SelectionParameterNames[49]);
        Assert.AreEqual(
            RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
    }

    /// <summary>
    /// The keyset carries the declared type, because one event contributes one row per type.
    /// </summary>
    /// <remarks>
    /// Without <c>key_2</c> two types of a single event share a cursor, and keyset pagination cannot
    /// advance past the second of them.
    /// </remarks>
    [TestMethod]
    public void TheKeysetCarriesTheDeclaredTypeSoOneEventsTypesCanBePagedApart()
    {
        var page = EuProcedureEventDiscoveryPlan.Create().PageTemplate;

        StringAssert.Contains(page, "BIND(STR(?event) AS ?key_1)");
        StringAssert.Contains(page, "BIND(COALESCE(STR(?event_type), \"\") AS ?key_3)");
        StringAssert.Contains(page, "BIND(STR(?dossier) AS ?key_5)");
        StringAssert.Contains(page, "BIND(COALESCE(STR(?event_date), \"\") AS ?key_6)");
        StringAssert.Contains(
            page, "ORDER BY ?key_1 ?key_2 ?key_3 ?key_4 ?key_5 ?key_6 ?key_7 ?key_8 ?key_9");
    }

    /// <summary>
    /// The untyped event is asked for explicitly, and its branch pins the triple rather than a word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Leaving <c>?event a ?event_type</c> mandatory made an event carrying the dossier edge and no
    /// <c>rdf:type</c> contribute NO ROW AT ALL. That is a silent drop, and it also made the accepted
    /// <see cref="EuProcedureEventRefusal.EventTypeMissing"/> a production path no real delivery could
    /// ever reach — the contract could refuse an untyped event only if somebody hand-built one.
    /// </para>
    /// <para>
    /// Pinned as whole triples in both templates for the same reason the date branch is: a guard
    /// asserting only that <c>a ?event_type</c> appears somewhere is satisfied by the mandatory form
    /// alone, which is the exact shape being repaired.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheUntypedEventBranchIsAskedForAndPinsItsOwnTriple()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(
                template, "FILTER NOT EXISTS { ?event a ?missing_type }",
                "the untyped event is asked about by name, never inferred from a row that never came.");
            StringAssert.Contains(
                template, "BIND(\"unbound\" AS ?type_kind)",
                "and it arrives carrying the marker that says so.");
        }
    }

    /// <summary>
    /// Every term the row groups on contributes to the canonical key, kind and qualifiers included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canonical key must be INJECTIVE over the grouped row: Source/Core requires canonical keys
    /// unique and cursors strictly increasing, so two distinct grouped rows sharing a key either
    /// refuse the whole page or cannot be paged across a boundary. A key built from <c>STR()</c>
    /// alone is not injective over terms — an IRI type and a literal type spelled identically share
    /// every lexical form, as do two date literals with one lexical value and different datatypes or
    /// language tags, and the accepted observation retains <c>DateDatatypeIri</c> and every type term,
    /// so those really are different facts.
    /// </para>
    /// <para>
    /// Derived from the template's own GROUP BY list rather than transcribed, so a variable added to
    /// the grouping and forgotten in the keyset fails here instead of waiting for a publisher to
    /// deliver the collision.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryGroupedTermContributesToTheCanonicalKeyIncludingItsTermAuthority()
    {
        var page = EuProcedureEventDiscoveryPlan.Create().PageTemplate;

        // The grouped row is a subquery inside the page, so its GROUP BY line is indented.
        var groupBy = page.Split('\n')
            .Select(static line => line.Trim())
            .Single(static line => line.StartsWith("GROUP BY ", StringComparison.Ordinal))
            .Substring("GROUP BY ".Length)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.IsGreaterThan(1, groupBy.Length, "the row groups on more than one term.");

        var keyBindings = page.Split('\n')
            .Where(static line => line.Contains(" AS ?key_", StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(9, keyBindings, "every cursor key is bound in the page template.");

        foreach (var variable in groupBy)
        {
            Assert.IsTrue(
                keyBindings.Any(binding => binding.Contains(
                    variable + ")", StringComparison.Ordinal)
                    || binding.Contains(variable + " AS ", StringComparison.Ordinal)),
                $"{variable} is grouped on but reaches no canonical key, so two rows differing only "
                    + "in it would collide.");
        }

        // The two collisions named in review, stated as the keys that separate them.
        StringAssert.Contains(page, "BIND(?type_kind AS ?key_4)",
            "an IRI type and a literal type spelled the same are separated by their kind.");
        StringAssert.Contains(page, "BIND(?date_datatype AS ?key_8)",
            "two date literals with one lexical value and different datatypes are separated.");
        StringAssert.Contains(page, "BIND(?date_language AS ?key_9)",
            "and so are two with different language tags.");
    }

    /// <summary>Binding fills every slot and sends no renderer placeholder.</summary>
    [TestMethod]
    public void BindingFillsEverySlotAndSendsNoPlaceholder()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();
        var source = Source();

        var count = plan.BindCount(
            EuProcedureEventQueryPass.Pass1, OneDossier,
            "urn:uuid:5e6f7081-92a3-4b4c-8d45-ef0123456789",
            "urn:uuid:6f708192-a3b4-4c5d-8e56-f0123456789a",
            source);
        var page = plan.BindPage(
            EuProcedureEventQueryPass.Pass2, OneDossier, null, 0, count.InputArtifact.ArtifactRef,
            "urn:uuid:708192a3-b4c5-4d6e-8f67-0123456789ab",
            "urn:uuid:8192a3b4-c5d6-4e7f-8078-123456789abc",
            source);

        foreach (var body in new[] { SentBody(count), SentBody(page) })
        {
            Assert.IsFalse(body.Contains(":iri}", StringComparison.Ordinal), "every batch slot is filled.");
            Assert.IsFalse(body.Contains("{pass_id:uint}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(":sparql_string}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("{page_limit:uint}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("{has_cursor:uint}", StringComparison.Ordinal));
            StringAssert.Contains(body, "3e485e15-11bd-11e6-ba9a-01aa75ed71a1");
        }
    }

    /// <summary>A cursor that is not this family's keyset is refused rather than padded.</summary>
    [TestMethod]
    public void ACursorThatIsNotTheKeysetIsRefused()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();
        var source = Source();
        var count = plan.BindCount(
            EuProcedureEventQueryPass.Pass1, OneDossier,
            "urn:uuid:92a3b4c5-d6e7-4f80-8189-23456789abcd",
            "urn:uuid:a3b4c5d6-e7f8-4091-829a-3456789abcde",
            source);

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            EuProcedureEventQueryPass.Pass1, OneDossier, ["only", "three", "parts"], 0,
            count.InputArtifact.ArtifactRef,
            "urn:uuid:b4c5d6e7-f809-41a2-83ab-456789abcdef",
            "urn:uuid:c5d6e7f8-091a-42b3-84bc-56789abcdef0",
            source));
    }

    /// <summary>A batch naming one dossier twice is refused, never silently deduplicated.</summary>
    /// <remarks>
    /// Canonicalisation folds <c>https://</c> and a trailing slash, so two spellings of one dossier
    /// are the same member. Accepting that batch would let a caller believe it asked about two.
    /// </remarks>
    [TestMethod]
    public void ABatchNamingOneDossierTwiceIsRefused()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindCount(
            EuProcedureEventQueryPass.Pass1,
            [
                "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
                "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1/",
            ],
            "urn:uuid:d6e7f809-1a2b-43c4-85cd-6789abcdef01",
            "urn:uuid:e7f8091a-2b3c-44d5-86de-789abcdef012",
            Source()));
    }

    /// <summary>
    /// The requested partition members are the canonical form the publisher is actually asked about.
    /// </summary>
    /// <remarks>
    /// The case-law family compared a delivered canonical key against the caller's raw spelling, so a
    /// caller writing <c>https://</c> would have had every honest row refused as outside its own
    /// partition. Exposing the asked-about form is what lets the executor compare like with like.
    /// </remarks>
    [TestMethod]
    public void TheRequestedPartitionMembersAreTheFormThePublisherIsAsked()
    {
        var admittedAlias =
            "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1/";

        CollectionAssert.AreEqual(
            new[] { "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1" },
            EuProcedureEventDiscoveryPlan.RequestedPartitionMembers([admittedAlias]).ToArray());
    }

    /// <summary>The plan's identity covers the queries it sends.</summary>
    /// <remarks>
    /// A plan whose identity did not cover its own query text could change what it asks while every
    /// retained artifact still named the same source, making the custody record a claim about a
    /// question nobody could reconstruct.
    /// </remarks>
    [TestMethod]
    public void TheCanonicalIdentityCoversTheQueriesItSends()
    {
        var plan = EuProcedureEventDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());

        StringAssert.Contains(identity, plan.CountTemplate.Trim());
        StringAssert.Contains(identity, plan.PageTemplate.Trim());
        StringAssert.Contains(identity, PartOfDossier);
        StringAssert.Contains(identity, EventDate);
        Assert.AreEqual(plan.ArtifactRef.Sha256, EuProcedureEventDiscoveryPlan.Create().ArtifactRef.Sha256);
    }
}
