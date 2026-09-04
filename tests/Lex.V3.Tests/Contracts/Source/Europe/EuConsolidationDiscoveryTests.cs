using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Europe;

[TestClass]
public sealed class EuConsolidationDiscoveryTests
{
    private const string BaseCelex = "32016R0679";
    private const string BaseWork =
        "http://publications.europa.eu/resource/cellar/11111111-1111-4111-8111-111111111111";
    private const string StateA =
        "http://publications.europa.eu/resource/cellar/22222222-2222-4222-8222-222222222222";
    private const string StateB =
        "http://publications.europa.eu/resource/cellar/33333333-3333-4333-8333-333333333333";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private static readonly byte[] RendererSourceBytes = Encoding.UTF8.GetBytes(
        "eu-consolidation-sparql-renderer-source/1\n");

    [TestMethod]
    public void ClosedPlanSeparatesFamilyAndFactDeliveryWithoutErasingMultiplicity()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var family = plan.Definition(EuConsolidationQuerySet.Family);
        var facts = plan.Definition(EuConsolidationQuerySet.TemporalFacts);

        Assert.AreNotEqual(family.CountQueryFamilyRef, family.PageQueryFamilyRef);
        Assert.AreNotEqual(facts.CountQueryFamilyRef, facts.PageQueryFamilyRef);
        Assert.AreNotEqual(family.PageQueryFamilyRef, facts.PageQueryFamilyRef);

        foreach (var query in new[]
                 {
                     family.CountTemplate, family.PageTemplate,
                     facts.CountTemplate, facts.PageTemplate,
                 })
        {
            Assert.IsFalse(query.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(query.Contains("OPTIONAL", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(query.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase));
        }

        StringAssert.Contains(family.PageTemplate,
            "act_consolidated_based_on_resource_legal");
        StringAssert.Contains(family.PageTemplate, "BIND(STR(?state) AS ?state_key)");
        StringAssert.Contains(family.PageTemplate, "ORDER BY ?state_key");
        StringAssert.Contains(family.PageTemplate, "COUNT(*) AS ?family_multiplicity");

        foreach (var localName in new[]
                 {
                     "act_consolidated_date", "act_consolidated_layer",
                     "act_consolidated_version", "act_consolidated_number",
                 })
        {
            StringAssert.Contains(facts.PageTemplate, localName);
        }

        StringAssert.Contains(facts.PageTemplate, "COUNT(?object) AS ?multiplicity");
        StringAssert.Contains(facts.PageTemplate, "FILTER NOT EXISTS");
        StringAssert.Contains(facts.PageTemplate, "unsupported_blank_node");
        StringAssert.Contains(facts.PageTemplate,
            "?object ?object_kind ?datatype_iri ?language_tag ?multiplicity");
        Assert.AreEqual(
            "b2a91efac90315df6730ca8ab6d00edcf6278aa3e912e11c784f4907acaa016d",
            plan.ArtifactRef.Sha256);
    }

    [TestMethod]
    public void DeliveryProfilesReuseTheSharedVerifierAndPinExactProjectionAndCursor()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var family = plan.CreateDeliveryProfile(EuConsolidationQuerySet.Family);
        var facts = plan.CreateDeliveryProfile(EuConsolidationQuerySet.TemporalFacts);

        Assert.AreEqual(
            RepeatedEnumerationSparqlJsonDialect.EuropeanUnionVirtuoso,
            family.Dialect);
        CollectionAssert.AreEqual(
            new[] { "base_celex", "base", "state", "family_multiplicity", "state_key" },
            family.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(new[] { "state_key" }, family.CursorVariables.ToArray());
        CollectionAssert.AreEqual(new[] { "requested_celex" },
            family.SelectionParameterNames.ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "base_celex", "base", "state", "predicate", "object", "object_kind",
                "datatype_iri", "language_tag", "multiplicity",
                "key_1", "key_2", "key_3", "key_4", "key_5", "key_6",
            },
            facts.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            facts.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3", "key_4", "key_5", "key_6" },
            facts.CanonicalKeyVariables.ToArray());
    }

    [TestMethod]
    public void TemporalFactsRunThroughTheSharedRetainedPayloadComparison()
    {
        var accepted = new TemporalDeliveryFixture().Create();

        Assert.AreEqual(EnumerationDeliveryOutcome.EqualSelections, accepted.Outcome);
        Assert.AreEqual(4L, accepted.SelectedRowCountA);
        Assert.AreEqual(4L, accepted.DeliveredRowCountA);
        Assert.AreEqual(2, accepted.PagesA.Pages.Count);
        Assert.AreEqual(2, accepted.PagesB.Pages.Count);

        var wrongCount = new TemporalDeliveryFixture(
            TemporalDeliveryMutation.WrongCount).Create();
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections, wrongCount.Outcome);
        Assert.AreEqual(5L, wrongCount.SelectedRowCountB);
        Assert.AreEqual(4L, wrongCount.DeliveredRowCountB);

        var changedMultiplicity = new TemporalDeliveryFixture(
            TemporalDeliveryMutation.ChangedMultiplicity).Create();
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections,
            changedMultiplicity.Outcome);
        Assert.AreNotEqual(changedMultiplicity.CanonicalRowDigestA,
            changedMultiplicity.CanonicalRowDigestB);
        Assert.AreEqual(changedMultiplicity.CanonicalKeyDigestA,
            changedMultiplicity.CanonicalKeyDigestB);

        var changedQualifier = new TemporalDeliveryFixture(
            TemporalDeliveryMutation.ChangedQualifier).Create();
        Assert.AreEqual(EnumerationDeliveryOutcome.DifferentSelections,
            changedQualifier.Outcome);
        Assert.AreNotEqual(changedQualifier.CanonicalKeyDigestA,
            changedQualifier.CanonicalKeyDigestB);

        foreach (var mutation in new[]
                 {
                     TemporalDeliveryMutation.WrongCursor,
                     TemporalDeliveryMutation.NonemptySuccessor,
                     TemporalDeliveryMutation.SamePass,
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new TemporalDeliveryFixture(mutation).Create(),
                mutation.ToString());
        }
    }

    [TestMethod]
    public void TheRendererProducesTheExactBytesItsReferencesName()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var rendererSource = RendererSource();
        var renderer = new EuConsolidationSparqlRenderer(
            plan,
            plan.Definition(EuConsolidationQuerySet.Family),
            isPage: false,
            rendererSource);

        var profile = renderer.CopyRendererProfileBytes();
        var source = renderer.CopyRendererSourceBytes();

        // Present and non-empty first. A renderer that produces nothing must return null, and the
        // conversion "bytes is null ? null : bytes" against a ReadOnlyMemory<byte>? target hands
        // back a present, EMPTY memory instead, which a digest assertion alone would not separate
        // from a genuine absence. Length is asserted before the digest for that reason.
        Assert.IsNotNull(profile);
        Assert.IsNotNull(source);
        Assert.IsTrue(profile.Value.Length > 0, "an empty profile offer is not an absent one");
        Assert.IsTrue(source.Value.Length > 0, "an empty source offer is not an absent one");
        Assert.AreEqual(plan.ArtifactRef, renderer.RendererProfileRef);
        Assert.AreEqual(rendererSource.Reference, renderer.RendererSourceRef);
        Assert.AreEqual(
            renderer.RendererProfileRef.Sha256,
            Convert.ToHexStringLower(SHA256.HashData(profile.Value.Span)),
            "the profile bytes must carry the digest the profile reference names");
        Assert.AreEqual(
            renderer.RendererSourceRef.Sha256,
            Convert.ToHexStringLower(SHA256.HashData(source.Value.Span)),
            "the source bytes must carry the digest the source reference names");

        // Aliasing, measured on the memory rather than on ToArray() of it. ToArray copies at the
        // call site, so an assertion written that way passes whether or not anything was copied.
        Assert.IsTrue(MemoryMarshal.TryGetArray(
            renderer.CopyRendererProfileBytes()!.Value, out var first));
        Assert.IsTrue(MemoryMarshal.TryGetArray(
            renderer.CopyRendererProfileBytes()!.Value, out var second));
        Assert.AreNotSame(
            first.Array,
            second.Array,
            "each call must hand back its own array, never the one being held");
        Assert.AreNotSame(
            plan.CopyCanonicalIdentityBytes(),
            plan.CopyCanonicalIdentityBytes(),
            "the plan's identity bytes are copied out, so a caller cannot edit the held array");
    }

    [TestMethod]
    public void BinderEmitsAnExactTypedCelexLiteralAndNeverAPlainLiteral()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var bound = plan.BindCount(
            EuConsolidationQuerySet.Family,
            BaseCelex,
            EuConsolidationQueryPass.Pass1,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            RendererSource());

        Assert.AreEqual(HttpRequestMethod.Post, bound.MachinePlan.Method);
        Assert.AreEqual(
            "application/sparql-query",
            bound.MachinePlan.ContentType!.MemberKey);
        Assert.AreEqual(
            MachineQueryParameterKind.PublisherLiteral,
            bound.InputArtifact.OrderedParameters[0].Kind);
        Assert.AreEqual(BaseCelex, bound.InputArtifact.OrderedParameters[0].TextValue);

        var query = System.Text.Encoding.UTF8.GetString(
            MachineQueryBinder.OpenForSend(bound.Request).CopyRequestBody());
        StringAssert.Contains(query,
            "\"32016R0679\"^^<http://www.w3.org/2001/XMLSchema#string>");
        Assert.IsFalse(query.Contains(
            "resource_legal_id_celex> \"32016R0679\" .",
            StringComparison.Ordinal));

        foreach (var outsideFrozenSeedScope in new[]
                 {
                     EuSeedResolutionPlan.PositiveControlCelex,
                     "32025R0001",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => plan.BindCount(
                EuConsolidationQuerySet.Family,
                outsideFrozenSeedScope,
                EuConsolidationQueryPass.Pass1,
                "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                RendererSource()));
        }

        var definition = plan.Definition(EuConsolidationQuerySet.Family);
        var response = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null);
        var replay = MachineQueryInputArtifact.Create(
            "urn:uuid:dddddddd-dddd-4ddd-8ddd-dddddddddddd",
            definition.CountQueryFamilyRef,
            "out-of-scope-control",
            response,
            new[]
            {
                new MachineQueryParameter(
                    "requested_celex", MachineQueryParameterKind.PublisherLiteral,
                    null, EuSeedResolutionPlan.PositiveControlCelex, plan.ArtifactRef),
                new MachineQueryParameter(
                    "pass_id", MachineQueryParameterKind.BoundedInteger,
                    1, null, plan.ArtifactRef),
            });
        var renderer = new EuConsolidationSparqlRenderer(
            plan, definition, isPage: false, RendererSource());
        Assert.ThrowsExactly<ArgumentException>(() =>
            renderer.RenderInput(replay, response));
    }

    [TestMethod]
    public void BothPageFamiliesRenderFirstAndContinuationRequestsFromTypedInputs()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var countEvidence = Artifact('d');
        var cases = new[]
        {
            (EuConsolidationQuerySet.Family, (IReadOnlyList<string>)new[] { StateA }),
            (EuConsolidationQuerySet.TemporalFacts, (IReadOnlyList<string>)new[]
            {
                StateA,
                EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                "literal",
                "001",
                XsdString,
                string.Empty,
            }),
        };

        foreach (var (set, cursor) in cases)
        {
            foreach (var value in new IReadOnlyList<string>?[] { null, cursor })
            {
                var bound = plan.BindPage(
                    set,
                    BaseCelex,
                    EuConsolidationQueryPass.Pass2,
                    value,
                    expectedPartitionRowCount: 41,
                    countEvidence,
                    "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                    "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                    RendererSource());
                var query = System.Text.Encoding.UTF8.GetString(
                    MachineQueryBinder.OpenForSend(bound.Request).CopyRequestBody());

                Assert.IsFalse(query.Contains(":uint}", StringComparison.Ordinal),
                    $"{set} left an integer renderer slot");
                Assert.IsFalse(query.Contains(":sparql_", StringComparison.Ordinal),
                    $"{set} left a text renderer slot");
                Assert.IsFalse(query.Contains(":typed_string}", StringComparison.Ordinal),
                    $"{set} left a typed-literal renderer slot");
                StringAssert.Contains(query, "LIMIT 613");
                Assert.AreEqual(value is null ? 0L : 1L,
                    bound.InputArtifact.OrderedParameters
                        .Single(static parameter => parameter.Name == "has_cursor")
                        .IntegerValue);
            }
        }
    }

    [TestMethod]
    public void ContinuationTermsUseTheSharedControlSafeSparqlStringEncoding()
    {
        var plan = EuConsolidationDiscoveryPlan.Create();
        var hostile = "tab\tline\nreturn\rback\bform\fdel\u007fquote\"slash\\";
        var bound = plan.BindPage(
            EuConsolidationQuerySet.Family,
            BaseCelex,
            EuConsolidationQueryPass.Pass1,
            new[] { hostile },
            expectedPartitionRowCount: 1,
            Artifact('d'),
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            RendererSource());

        var query = System.Text.Encoding.UTF8.GetString(
            MachineQueryBinder.OpenForSend(bound.Request).CopyRequestBody());
        StringAssert.Contains(query,
            "tab\\tline\\nreturn\\rback\\bform\\fdel\\u007Fquote\\\"slash\\\\");
        Assert.IsFalse(query.Contains(hostile, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FamilyRowRequiresReturnedTypedCelexAndOfficialCellarWorkUris()
    {
        var valid = EuConsolidationFamilyRow.Parse(
            Row(Celex(), Iri(BaseWork), Iri(StateA), Integer("2"), Plain(StateA)),
            BaseCelex);

        Assert.AreEqual(BaseCelex, valid.BaseCelex.Value);
        Assert.AreEqual(BaseWork, valid.BaseWork.Value);
        Assert.AreEqual(StateA, valid.State.Value);
        Assert.AreEqual(2L, valid.Multiplicity);
        Assert.ThrowsExactly<ArgumentException>(() => EuConsolidationFamilyRow.Parse(
            Row(
                RepeatedEnumerationRdfTerm.Literal("32016R1011", XsdString, null),
                Iri(BaseWork), Iri(StateA), Integer("2"), Plain(StateA)),
            BaseCelex));

        foreach (var hostileCelex in new[]
                 {
                     RepeatedEnumerationRdfTerm.Literal(BaseCelex, null, null),
                     RepeatedEnumerationRdfTerm.Literal(BaseCelex, null, "en"),
                     RepeatedEnumerationRdfTerm.Literal(BaseCelex, XsdDate, null),
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                EuConsolidationFamilyRow.Parse(Row(
                    hostileCelex,
                    Iri(BaseWork),
                    Iri(StateA),
                    Integer("2"),
                    Plain(StateA)), BaseCelex));
        }

        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationFamilyRow.Parse(Row(
                Celex(),
                Iri("https://example.invalid/resource/cellar/11111111-1111-4111-8111-111111111111"),
                Iri(StateA), Integer("2"), Plain(StateA)), BaseCelex));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationFamilyRow.Parse(Row(
                Celex(), Iri(BaseWork), Iri(StateA.ToUpperInvariant()),
                Integer("2"), Plain(StateA.ToUpperInvariant())), BaseCelex));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationFamilyRow.Parse(Row(
                Celex(), Iri(BaseWork), Iri(StateA), Integer("2"), Plain(StateB)),
                BaseCelex));
    }

    [TestMethod]
    public void TemporalFactKeepsTheExactRdfTermQualifiersAndMultiplicity()
    {
        var rawDate = RepeatedEnumerationRdfTerm.Literal(
            "2024-01-01+01:00", XsdDate, null);
        var fact = EuConsolidationFactRow.Parse(FactRow(
            StateA,
            EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
            rawDate,
            3), BaseCelex);

        Assert.AreSame(rawDate, fact.Object);
        Assert.AreEqual("2024-01-01+01:00", fact.Object.Value);
        Assert.AreEqual(XsdDate, fact.Object.Datatype);
        Assert.IsNull(fact.Object.Language);
        Assert.AreEqual("literal", fact.ObjectKind);
        Assert.AreEqual(XsdDate, fact.DatatypeIri);
        Assert.AreEqual(string.Empty, fact.LanguageTag);
        Assert.AreEqual(3L, fact.Multiplicity);

        var language = EuConsolidationFactRow.Parse(FactRow(
            StateA,
            EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
            RepeatedEnumerationRdfTerm.Literal("couche", null, "fr"),
            1), BaseCelex);
        Assert.AreEqual(
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString",
            language.DatatypeIri);
        Assert.AreEqual("fr", language.LanguageTag);

        var unbound = EuConsolidationFactRow.Parse(FactRow(
            StateA,
            EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
            RepeatedEnumerationRdfTerm.Unbound(),
            0), BaseCelex);
        Assert.AreEqual(RepeatedEnumerationRdfTermKind.Unbound, unbound.Object.Kind);
        Assert.AreEqual(0L, unbound.Multiplicity);

        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationFactRow.Parse(FactRow(
                StateA,
                "http://publications.europa.eu/ontology/cdm#resource_legal_in-force",
                rawDate,
                1), BaseCelex));
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationFactRow.Parse(FactRow(
                StateA,
                EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                RepeatedEnumerationRdfTerm.BlankNode("publisher-node"),
                1), BaseCelex));

        Assert.ThrowsExactly<ArgumentException>(() => EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            new[]
            {
                ParsedFact(StateB, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    rawDate),
            }));
        Assert.ThrowsExactly<ArgumentException>(() => EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            new[]
            {
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdString, null)),
            }));
    }

    [TestMethod]
    public void MissingFieldsStayUnboundAndSameDateCandidatesAreNeverSilentlyOrdered()
    {
        var first = EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            new[]
            {
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdDate, null)),
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                    Plain("001"), 2),
                UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri),
                UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri),
            });
        var second = EuConsolidationSelectedState.From(
            FamilyRow(StateB),
            new[]
            {
                ParsedFact(StateB, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdDate, null)),
                ParsedFact(StateB, EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                    Plain("002")),
                ParsedFact(StateB, EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
                    Plain("003")),
                ParsedFact(StateB, EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri,
                    Plain("2016R0679/20240101_0000030")),
            });

        Assert.AreEqual(RepeatedEnumerationRdfTermKind.Unbound,
            first.Version.Single().Term.Kind);
        Assert.AreEqual(RepeatedEnumerationRdfTermKind.Unbound,
            first.Number.Single().Term.Kind);
        Assert.AreEqual(2L, first.Layer.Single().Multiplicity);

        var assessment = EuConsolidationSameDateAssessment.From(new[] { first, second });
        var date = assessment.BoundDates.Single();
        Assert.AreEqual(EuConsolidationDateStatus.AmbiguousVersion, date.Status);
        CollectionAssert.AreEqual(new[] { StateA, StateB },
            date.Candidates.Select(static candidate => candidate.State.Value).ToArray());
        Assert.IsNull(typeof(EuConsolidationSameDateGroup).GetProperty(
            "Selected", BindingFlags.Public | BindingFlags.Instance));
        Assert.IsNull(typeof(EuConsolidationSameDateGroup).GetProperty(
            "Winner", BindingFlags.Public | BindingFlags.Instance));
    }

    [TestMethod]
    public void GroupedMultiplicityIsRetainedAndDuplicateExactRowsAreRefused()
    {
        var facts = new[]
        {
            ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdDate, null), 5),
            ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                Plain("001")),
            UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri),
            UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri),
        };
        var selected = EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            facts);

        Assert.AreEqual(5L, selected.Date.Single().Multiplicity);
        var group = EuConsolidationSameDateAssessment.From(new[] { selected }).BoundDates.Single();
        Assert.AreEqual(EuConsolidationDateStatus.OneObservedCandidate, group.Status);
        Assert.AreEqual(1, group.Candidates.Count);

        Assert.ThrowsExactly<ArgumentException>(() => EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            facts.Prepend(facts[0]).ToArray()));
    }

    [TestMethod]
    public void SameDateAssessmentRefusesMixedCoordinatesAndDuplicateStateInputs()
    {
        var first = CompleteState(StateA, BaseWork, BaseCelex);
        var duplicate = CompleteState(StateA, BaseWork, BaseCelex);
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationSameDateAssessment.From(new[] { first, duplicate }));

        var otherBase =
            "http://publications.europa.eu/resource/cellar/44444444-4444-4444-8444-444444444444";
        var mixed = CompleteState(StateB, otherBase, "32016R1011");
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuConsolidationSameDateAssessment.From(new[] { first, mixed }));
    }

    [TestMethod]
    public void EveryDateOfAMultiplyDatedStateRemainsExplicitlyAmbiguous()
    {
        var selected = EuConsolidationSelectedState.From(
            FamilyRow(StateA),
            new[]
            {
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdDate, null)),
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    RepeatedEnumerationRdfTerm.Literal("2024-02-01", XsdDate, null)),
                ParsedFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                    Plain("001")),
                UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri),
                UnboundFact(StateA, EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri),
            });

        var groups = EuConsolidationSameDateAssessment.From(new[] { selected }).BoundDates;
        Assert.AreEqual(2, groups.Count);
        Assert.IsTrue(groups.All(static group =>
            group.Status == EuConsolidationDateStatus.AmbiguousVersion));
        Assert.IsTrue(groups.All(group => group.Candidates.Single() == selected));
    }

    private static EuConsolidationFamilyRow FamilyRow(string state) =>
        EuConsolidationFamilyRow.Parse(Row(
            Celex(), Iri(BaseWork), Iri(state), Integer("2"), Plain(state)), BaseCelex);

    private static EuConsolidationFactRow ParsedFact(
        string state,
        string predicate,
        RepeatedEnumerationRdfTerm value,
        long multiplicity = 1) =>
        EuConsolidationFactRow.Parse(
            FactRow(state, predicate, value, multiplicity), BaseCelex);

    private static EuConsolidationFactRow UnboundFact(string state, string predicate) =>
        ParsedFact(state, predicate, RepeatedEnumerationRdfTerm.Unbound(), 0);

    private static EuConsolidationSelectedState CompleteState(
        string state,
        string baseWork,
        string celex)
    {
        var family = EuConsolidationFamilyRow.Parse(Row(
            RepeatedEnumerationRdfTerm.Literal(celex, XsdString, null),
            Iri(baseWork), Iri(state), Integer("1"), Plain(state)), celex);
        return EuConsolidationSelectedState.From(family, new[]
        {
            Fact(state, baseWork, celex, EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                RepeatedEnumerationRdfTerm.Literal("2024-01-01", XsdDate, null), 1),
            Fact(state, baseWork, celex, EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                Plain("001"), 1),
            Fact(state, baseWork, celex, EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
                RepeatedEnumerationRdfTerm.Unbound(), 0),
            Fact(state, baseWork, celex, EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri,
                RepeatedEnumerationRdfTerm.Unbound(), 0),
        });
    }

    private static RepeatedEnumerationRow FactRow(
        string state,
        string predicate,
        RepeatedEnumerationRdfTerm value,
        long multiplicity)
        => FactRow(BaseWork, BaseCelex, state, predicate, value, multiplicity);

    private static EuConsolidationFactRow Fact(
        string state,
        string baseWork,
        string celex,
        string predicate,
        RepeatedEnumerationRdfTerm value,
        long multiplicity) => EuConsolidationFactRow.Parse(
        FactRow(baseWork, celex, state, predicate, value, multiplicity), celex);

    private static RepeatedEnumerationRow FactRow(
        string baseWork,
        string celex,
        string state,
        string predicate,
        RepeatedEnumerationRdfTerm value,
        long multiplicity)
    {
        var kind = value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
            _ => "unbound",
        };
        var datatype = value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri or RepeatedEnumerationRdfTermKind.BlankNode or
                RepeatedEnumerationRdfTermKind.Unbound =>
                string.Empty,
            _ => value.Datatype ?? (value.Language is null
                ? XsdString
                : "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"),
        };
        var language = value.Language ?? string.Empty;
        return Row(
            RepeatedEnumerationRdfTerm.Literal(celex, XsdString, null),
            Iri(baseWork), Iri(state), Iri(predicate), value,
            Plain(kind), Plain(datatype), Plain(language), Integer(multiplicity.ToString()),
            Plain(state), Plain(predicate), Plain(kind), Plain(value.Value ?? string.Empty),
            Plain(datatype), Plain(language));
    }

    private static RepeatedEnumerationRow Row(params RepeatedEnumerationRdfTerm[] terms) =>
        new(terms, terms, new[] { terms[^1] });

    private static RepeatedEnumerationRdfTerm Celex() =>
        RepeatedEnumerationRdfTerm.Literal(BaseCelex, XsdString, null);

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Plain(string value) =>
        RepeatedEnumerationRdfTerm.Literal(value, null, null);

    private static RepeatedEnumerationRdfTerm Integer(string value) =>
        RepeatedEnumerationRdfTerm.Literal(value, XsdInteger, null);

    private enum TemporalDeliveryMutation
    {
        None,
        WrongCount,
        WrongCursor,
        ChangedMultiplicity,
        ChangedQualifier,
        NonemptySuccessor,
        SamePass,
    }

    private sealed class TemporalDeliveryFixture : IRepeatedEnumerationEvidenceResolver
    {
        private const string ResponseMediaType = "application/sparql-results+json";
        private readonly Dictionary<SourceArtifactRef, RepeatedEnumerationResolvedEvidence>
            _evidence = [];
        private readonly TemporalDeliveryMutation _mutation;
        private readonly EuConsolidationDiscoveryPlan _plan =
            EuConsolidationDiscoveryPlan.Create();
        private readonly MachineQueryRendererSource _rendererSource =
            MachineQueryRendererSource.Open(
                Reference(700, RendererSourceBytes),
                RendererSourceBytes);
        private int _seed = 100;
        private ulong _requestOrdinal;

        internal TemporalDeliveryFixture(
            TemporalDeliveryMutation mutation = TemporalDeliveryMutation.None)
        {
            _mutation = mutation;
        }

        internal EnumerationDeliveryComparison Create()
        {
            var a = AddPass(EuConsolidationQueryPass.Pass1, FactsPayload());
            var passB = _mutation == TemporalDeliveryMutation.SamePass
                ? EuConsolidationQueryPass.Pass1
                : EuConsolidationQueryPass.Pass2;
            var bRows = _mutation switch
            {
                TemporalDeliveryMutation.ChangedMultiplicity => FactsPayload(dateMultiplicity: 2),
                TemporalDeliveryMutation.ChangedQualifier => FactsPayload(layerLanguage: "de"),
                _ => FactsPayload(),
            };
            var b = AddPass(
                passB,
                bRows,
                count: _mutation == TemporalDeliveryMutation.WrongCount ? 5 : 4,
                wrongCursor: _mutation == TemporalDeliveryMutation.WrongCursor,
                nonemptySuccessor: _mutation == TemporalDeliveryMutation.NonemptySuccessor);
            var profile = _plan.CreateDeliveryProfile(
                EuConsolidationQuerySet.TemporalFacts);
            var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
                Artifact(701).ResourceId,
                profile);
            return EnumerationDeliveryComparison.Create(
                profile,
                profileRef,
                a.Count,
                new(a.Pages),
                b.Count,
                new(b.Pages),
                this);
        }

        public RepeatedEnumerationResolvedEvidence Resolve(
            RepeatedEnumerationEvidenceRefs references) =>
            _evidence.TryGetValue(references.HttpEvidenceRef, out var value)
                ? value
                : throw new ArgumentException("The retained S9 evidence is missing.",
                    nameof(references));

        private (RepeatedEnumerationEvidenceRefs Count,
            IReadOnlyList<RepeatedEnumerationPageRef> Pages) AddPass(
            EuConsolidationQueryPass pass,
            string rows,
            long count = 4,
            bool wrongCursor = false,
            bool nonemptySuccessor = false)
        {
            var countBound = _plan.BindCount(
                EuConsolidationQuerySet.TemporalFacts,
                BaseCelex,
                pass,
                Artifact(++_seed).ResourceId,
                Artifact(++_seed).ResourceId,
                _rendererSource);
            var countRefs = Add(countBound, CountPayload(count), isPage: false);
            var firstBound = _plan.BindPage(
                EuConsolidationQuerySet.TemporalFacts,
                BaseCelex,
                pass,
                null,
                count,
                countRefs.HttpEvidenceRef,
                Artifact(++_seed).ResourceId,
                Artifact(++_seed).ResourceId,
                _rendererSource);
            var firstRefs = Add(firstBound, rows, isPage: true);
            var cursor = new[]
            {
                StateA,
                EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
                "literal",
                wrongCursor ? "wrong" : "001",
                XsdString,
                string.Empty,
            };
            var successorBound = _plan.BindPage(
                EuConsolidationQuerySet.TemporalFacts,
                BaseCelex,
                pass,
                cursor,
                count,
                countRefs.HttpEvidenceRef,
                Artifact(++_seed).ResourceId,
                Artifact(++_seed).ResourceId,
                _rendererSource);
            var successor = nonemptySuccessor
                ? FactsPayload(onlyLaterVersion: true)
                : EmptyFactsPayload();
            var successorRefs = Add(successorBound, successor, isPage: true);
            return (countRefs, new[]
            {
                new RepeatedEnumerationPageRef(0, firstRefs),
                new RepeatedEnumerationPageRef(1, successorRefs),
            });
        }

        private RepeatedEnumerationEvidenceRefs Add(
            EuConsolidationBoundQuery bound,
            string payload,
            bool isPage)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var opened = MachineQueryBinder.OpenForSend(bound.Request);
            var receipt = opened.RenderReceipt;
            var receiptRef = MachineQueryRenderReceiptIdentity.Create(
                Artifact(++_seed).ResourceId,
                receipt);
            var sourceProfile = OfficialMachineQuerySourceProfiles.ResolveFor(opened);
            var requestBody = opened.CopyRequestBody();
            var logicalRequest = HttpLogicalRequest.Create(
                opened.RequestedUri,
                sourceProfile.Method,
                new[]
                {
                    new HttpLogicalRequestHeader(
                        "user-agent",
                        sourceProfile.CrawlerUserAgent),
                    new HttpLogicalRequestHeader("accept", sourceProfile.Accept!),
                    new HttpLogicalRequestHeader(
                        "content-type",
                        $"{sourceProfile.RequestContentType}; charset=utf-8"),
                },
                new HttpLogicalRequestBody(
                    checked((ulong)requestBody.LongLength),
                    Sha(requestBody)),
                Artifact(702).Sha256,
                Artifact(703).Sha256);
            var logicalRequestRef = Reference(
                ++_seed,
                logicalRequest.CopyCanonicalBytes());
            var digest = Sha(bytes);
            var blob = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                digest,
                bytes.LongLength,
                CustodyClass.NightlyFloor90d);
            var instant = DateTimeOffset.UnixEpoch.AddSeconds(_seed);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                blob,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse($"00000000-0000-4000-8000-{_seed:D12}"),
                CustodyProtection.LockedTime,
                instant,
                instant.AddDays(91));
            var write = new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                blob,
                policy);
            var absent = new RoutedHttpAbsentHeader();
            var headers = new RoutedHttpResponseHeaders(
                new RoutedHttpSingleHeader(ResponseMediaType),
                new RoutedHttpSingleHeader(bytes.LongLength.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                absent,
                absent,
                absent,
                absent,
                absent,
                absent,
                absent,
                absent,
                absent,
                absent,
                absent);
            var observationId = Artifact(++_seed).ResourceId;
            var hop = RoutedHttpHop.Create(
                ordinal: 0,
                observationId,
                antecedentHopObservationId: null,
                logicalRequestRef.Sha256,
                logicalRequest.Uri,
                status: 200,
                headers,
                Timestamp(instant),
                Timestamp(instant.AddMilliseconds(1)),
                new DeclaredContentLengthHttpCompletion(checked((ulong)bytes.LongLength)),
                checked((ulong)bytes.LongLength),
                digest,
                Sha(Encoding.UTF8.GetBytes(ContractJson.Serialize(write))),
                checked((ulong)bytes.LongLength),
                digest);
            var httpEvidence = RoutedHttpEvidence.Create(
                Artifact(704),
                ++_requestOrdinal,
                attemptOrdinal: 0,
                new[] { hop },
                new CompleteHttpRouteOutcome(),
                new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)
                {
                    [hop.ObservationId] = write,
                });
            var httpEvidenceRef = Reference(
                ++_seed,
                httpEvidence.CopyCanonicalBytes());
            var renderer = new EuConsolidationSparqlRenderer(
                _plan,
                _plan.Definition(EuConsolidationQuerySet.TemporalFacts),
                isPage,
                _rendererSource);
            var refs = new RepeatedEnumerationEvidenceRefs(
                bound.MachinePlanRef,
                bound.InputArtifact.ArtifactRef,
                receiptRef,
                logicalRequestRef,
                httpEvidenceRef);
            _evidence.Add(httpEvidenceRef, new RepeatedEnumerationResolvedEvidence(
                bound.MachinePlan,
                bound.InputArtifact,
                receipt,
                renderer,
                logicalRequest,
                httpEvidence,
                write,
                bytes));
            return refs;
        }

        private static string CountPayload(long count) =>
            "{\"head\":{\"link\":[],\"vars\":[\"count\"]}," +
            "\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[{" +
            "\"count\":{" + Term("literal", count.ToString(
                System.Globalization.CultureInfo.InvariantCulture), XsdInteger)[1..] +
            "}]}}";

        private static string EmptyFactsPayload() => FactsDocument([]);

        private static string FactsPayload(
            long dateMultiplicity = 1,
            string layerLanguage = "fr",
            bool onlyLaterVersion = false)
        {
            if (onlyLaterVersion)
            {
                return FactsDocument(new[]
                {
                    FactBinding(
                        EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
                        Term("literal", "002", XsdString),
                        "literal", XsdString, string.Empty, 1),
                });
            }

            return FactsDocument(new[]
            {
                FactBinding(
                    EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri,
                    Term("literal", "2024-01-01", XsdDate),
                    "literal", XsdDate, string.Empty, dateMultiplicity),
                FactBinding(
                    EuConsolidationDiscoveryPlan.ConsolidatedLayerPredicateIri,
                    null,
                    "unbound", string.Empty, string.Empty, 0),
                FactBinding(
                    EuConsolidationDiscoveryPlan.ConsolidatedNumberPredicateIri,
                    Term("literal", "number", language: layerLanguage),
                    "literal", EuConsolidationTerm.RdfLangStringDatatypeIri,
                    layerLanguage, 1),
                FactBinding(
                    EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri,
                    Term("literal", "001", XsdString),
                    "literal", XsdString, string.Empty, 1),
            });
        }

        private static string FactsDocument(IReadOnlyList<string> rows)
        {
            var variables = EuConsolidationDiscoveryPlan.Create()
                .Definition(EuConsolidationQuerySet.TemporalFacts)
                .ProjectionVariables;
            return "{\"head\":{\"link\":[],\"vars\":" +
                   JsonSerializer.Serialize(variables) + "}," +
                   "\"results\":{\"distinct\":false,\"ordered\":true," +
                   "\"bindings\":[" + string.Join(',', rows) + "]}}";
        }

        private static string FactBinding(
            string predicate,
            string? objectTerm,
            string objectKind,
            string datatype,
            string language,
            long multiplicity)
        {
            var objectValue = objectTerm is null
                ? string.Empty
                : ",\"object\":" + objectTerm;
            var keyValue = objectTerm is null
                ? string.Empty
                : objectKind == "literal" && language.Length != 0
                    ? "number"
                    : predicate == EuConsolidationDiscoveryPlan.ConsolidatedDatePredicateIri
                        ? "2024-01-01"
                        : predicate == EuConsolidationDiscoveryPlan.ConsolidatedVersionPredicateIri
                            ? objectTerm.Contains("002", StringComparison.Ordinal) ? "002" : "001"
                            : string.Empty;
            return "{" +
                   "\"base_celex\":" + Term("literal", BaseCelex, XsdString) + "," +
                   "\"base\":" + Term("uri", BaseWork) + "," +
                   "\"state\":" + Term("uri", StateA) + "," +
                   "\"predicate\":" + Term("uri", predicate) + objectValue + "," +
                   "\"object_kind\":" + Term("literal", objectKind) + "," +
                   "\"datatype_iri\":" + Term("literal", datatype) + "," +
                   "\"language_tag\":" + Term("literal", language) + "," +
                   "\"multiplicity\":" + Term("literal", multiplicity.ToString(
                       System.Globalization.CultureInfo.InvariantCulture), XsdInteger) + "," +
                   "\"key_1\":" + Term("literal", StateA) + "," +
                   "\"key_2\":" + Term("literal", predicate) + "," +
                   "\"key_3\":" + Term("literal", objectKind) + "," +
                   "\"key_4\":" + Term("literal", keyValue) + "," +
                   "\"key_5\":" + Term("literal", datatype) + "," +
                   "\"key_6\":" + Term("literal", language) + "}";
        }

        private static string Term(
            string type,
            string value,
            string? datatype = null,
            string? language = null) =>
            "{\"type\":" + JsonSerializer.Serialize(type) +
            ",\"value\":" + JsonSerializer.Serialize(value) +
            (datatype is null ? string.Empty :
                ",\"datatype\":" + JsonSerializer.Serialize(datatype)) +
            (language is null ? string.Empty :
                ",\"xml:lang\":" + JsonSerializer.Serialize(language)) + "}";

        private static SourceArtifactRef Artifact(int seed) => new(
            $"urn:uuid:00000000-0000-4000-8000-{seed:D12}",
            seed.ToString("x64", System.Globalization.CultureInfo.InvariantCulture));

        private static SourceArtifactRef Reference(int seed, ReadOnlySpan<byte> bytes) =>
            new(Artifact(seed).ResourceId, Sha(bytes));

        private static string Timestamp(DateTimeOffset value) =>
            value.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);

        private static string Sha(ReadOnlySpan<byte> value) =>
            Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static SourceArtifactRef Artifact(char value) => new(
        $"urn:uuid:{value}{value}{value}{value}{value}{value}{value}{value}-{value}{value}{value}{value}-4{value}{value}{value}-8{value}{value}{value}-{new string(value, 12)}",
        new string(value, 64));

    // The renderer source is a pair now, so its fixture reference has to be minted from real
    // bytes: Artifact('c') named a digest that no artifact carries, which Open refuses.
    private static MachineQueryRendererSource RendererSource() =>
        MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                Artifact('c').ResourceId,
                Convert.ToHexStringLower(SHA256.HashData(RendererSourceBytes))),
            RendererSourceBytes);

    // ---- Construction surface (design fix three). These four types were widened from internal to
    // public alongside D1-05c-1's own EuObjectFactsDiscoveryPlan family (a caller outside this
    // assembly now binds a pass through the plan's own now-public BindCount/BindPage), and none of
    // them carried a pin before. Values transcribed literally from ConstructionSurface.Of, the same
    // print-then-transcribe discipline EuObjectFactsDiscoveryPlanTests uses for its own four types. ----

    private const string SurfaceN = "Lex.V3.Contracts.Source.Europe.";
    private const string SurfaceCore = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void ThePlanHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + SurfaceN + "EuConsolidationDiscoveryPlan::.ctor() -> "
                    + SurfaceN + "EuConsolidationDiscoveryPlan",
                "constructor private static " + SurfaceN + "EuConsolidationDiscoveryPlan::.cctor() -> "
                    + SurfaceN + "EuConsolidationDiscoveryPlan",
                "method public static " + SurfaceN + "EuConsolidationDiscoveryPlan::Create() -> "
                    + SurfaceN + "EuConsolidationDiscoveryPlan",
            },
            ConstructionSurface.Of(typeof(EuConsolidationDiscoveryPlan)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuConsolidationDiscoveryPlan).Assembly, typeof(EuConsolidationDiscoveryPlan), true).ToArray(),
            "nothing else in Contracts may hand out a plan it did not create");
    }

    [TestMethod]
    public void TheQuerySetEnumHasExactlyTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + SurfaceN + "EuConsolidationQuerySet::Family -> "
                    + SurfaceN + "EuConsolidationQuerySet",
                "field public static " + SurfaceN + "EuConsolidationQuerySet::TemporalFacts -> "
                    + SurfaceN + "EuConsolidationQuerySet",
            },
            ConstructionSurface.Of(typeof(EuConsolidationQuerySet)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + SurfaceN + "EuConsolidationDiscoveryPlan::_definitions -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<" + SurfaceN + "EuConsolidationQuerySet, "
                    + SurfaceN + "EuConsolidationQueryDefinition>",
                "field private instance " + SurfaceN + "EuConsolidationQueryDefinition::<Set>k__BackingField -> "
                    + SurfaceN + "EuConsolidationQuerySet",
                "property internal instance " + SurfaceN + "EuConsolidationQueryDefinition::Set() -> "
                    + SurfaceN + "EuConsolidationQuerySet",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuConsolidationDiscoveryPlan).Assembly, typeof(EuConsolidationQuerySet), true).ToArray());
    }

    [TestMethod]
    public void TheQueryPassEnumHasExactlyTwoMembers()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + SurfaceN + "EuConsolidationQueryPass::Pass1 -> "
                    + SurfaceN + "EuConsolidationQueryPass",
                "field public static " + SurfaceN + "EuConsolidationQueryPass::Pass2 -> "
                    + SurfaceN + "EuConsolidationQueryPass",
            },
            ConstructionSurface.Of(typeof(EuConsolidationQueryPass)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuConsolidationDiscoveryPlan).Assembly, typeof(EuConsolidationQueryPass), true).ToArray(),
            "nothing else in Contracts distinguishes a pass otherwise; only Set does, via the definitions map");
    }

    [TestMethod]
    public void TheBoundQueryRecordHasExactlyItsOwnPrimaryConstructorAndCopyDoors()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + SurfaceN + "EuConsolidationBoundQuery::.ctor("
                    + SurfaceN + "EuConsolidationBoundQuery) -> " + SurfaceN + "EuConsolidationBoundQuery",
                "constructor public instance " + SurfaceN + "EuConsolidationBoundQuery::.ctor("
                    + SurfaceCore + "MachineQueryPlan, " + SurfaceCore + "SourceArtifactRef, "
                    + SurfaceCore + "MachineQueryInputArtifact, " + SurfaceCore + "BoundMachineRequest) -> "
                    + SurfaceN + "EuConsolidationBoundQuery",
                "method public instance " + SurfaceN + "EuConsolidationBoundQuery::<Clone>$() -> "
                    + SurfaceN + "EuConsolidationBoundQuery",
            },
            ConstructionSurface.Of(typeof(EuConsolidationBoundQuery)).ToArray());

        // The plan's own private Bind is the one helper both public entry points route through -
        // a real external door the sweep is right to report alongside BindCount and BindPage,
        // not a shape to guess down to just the two public methods.
        CollectionAssert.AreEqual(
            new[]
            {
                "method private instance " + SurfaceN + "EuConsolidationDiscoveryPlan::Bind("
                    + SurfaceN + "EuConsolidationQueryDefinition, System.Boolean, System.String, "
                    + SurfaceN + "EuConsolidationQueryPass, "
                    + "System.Collections.Generic.IReadOnlyList<System.String>, "
                    + SurfaceCore + "MachineResponseCardinality, System.String, System.String, "
                    + SurfaceCore + "MachineQueryRendererSource) -> " + SurfaceN + "EuConsolidationBoundQuery",
                "method public instance " + SurfaceN + "EuConsolidationDiscoveryPlan::BindCount("
                    + SurfaceN + "EuConsolidationQuerySet, System.String, " + SurfaceN + "EuConsolidationQueryPass, "
                    + "System.String, System.String, " + SurfaceCore + "MachineQueryRendererSource) -> "
                    + SurfaceN + "EuConsolidationBoundQuery",
                "method public instance " + SurfaceN + "EuConsolidationDiscoveryPlan::BindPage("
                    + SurfaceN + "EuConsolidationQuerySet, System.String, " + SurfaceN + "EuConsolidationQueryPass, "
                    + "System.Collections.Generic.IReadOnlyList<System.String>, System.Int64, "
                    + SurfaceCore + "SourceArtifactRef, System.String, System.String, "
                    + SurfaceCore + "MachineQueryRendererSource) -> " + SurfaceN + "EuConsolidationBoundQuery",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuConsolidationDiscoveryPlan).Assembly, typeof(EuConsolidationBoundQuery), true).ToArray());
    }
}
