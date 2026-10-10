using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Round-trip and hostile fixtures for the publisher-fact graph.
///
/// The mutation receipts are the point of this file. Each entry in
/// <see cref="MutationReceipts"/> names a way a future change could quietly lose an
/// official assertion, and names the test that fails when it is made. A contract that
/// merely serialises correctly today proves nothing about what a refactor will do to it,
/// and a passing test that has never been watched to fail proves nothing at all.
/// </summary>
[TestClass]
public sealed class PublisherFactsTests
{
    /// <summary>
    /// The mutation-to-test map, asserted for completeness by
    /// <see cref="EveryDeclaredMutationNamesATestThatExists"/>. Adding a fact member
    /// without adding its receipt leaves a member no mutation covers.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> MutationReceipts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["M01 drop the required origin of a derived inverse"] =
                nameof(ADerivedInverseWithoutItsOriginIsRefused),
            ["M02 drop the required observation of an assertion"] =
                nameof(AFactWithoutItsObservationIsRefused),
            ["M03 collapse the axiom multimap to one entry per predicate"] =
                nameof(DuplicateAxiomPredicatesBothSurvive),
            ["M04 deduplicate axioms ignoring occurrence identity"] =
                nameof(TwoByteIdenticalAxiomsRemainTwoAxioms),
            ["M05 drop occurrence identity from assertion equality"] =
                nameof(TwoByteIdenticalPublisherRowsRemainDistinctByOccurrence),
            ["M06 sort the axiom multimap"] =
                nameof(EqualityDistinguishesAxiomOrderAndDuplicates),
            ["M07 sort arrays inside canonical bytes"] =
                nameof(CanonicalBytesNeverSortAnArray),
            ["M08 accept an inverse with no authorising ontology"] =
                nameof(ADerivedInverseWithoutAnAuthorisingOntologyIsRefused),
            ["M09 treat the union tag as advisory and try every variant"] =
                nameof(AVariantCannotDeserialiseAsAnother),
            ["M10 infer a date role from position"] =
                nameof(TheDateRoleIsNeverInferredFromPosition),
            ["M11 add an inferred interval member to a relation"] =
                nameof(ModifiedTempByCarriesNoInferredInterval),
            ["M12 resolve unsupported vocabulary to a default instead of drift"] =
                nameof(UnsupportedVocabularyResolvesToDriftRatherThanADefault),
            ["M13 accept a closed vocabulary member as an ordinal number"] =
                nameof(AnIntegerUnheldReasonFailsClosed),
            ["M14 stop refusing members the schema forbids"] =
                nameof(AnUnmappedMemberIsRefused),
            ["M15 keep the last of two duplicate members"] =
                nameof(ADuplicateMemberIsRefused),
            ["M16 drop the union tag from canonical identity"] =
                nameof(CanonicalBytesCarryTheUnionTag),
            ["M17 reintroduce a storage locator into the fact graph"] =
                nameof(NoFactMemberNamesAStorageCoordinate),
            ["M18 map the variant property onto the wire tag so a stray tag is skipped"] =
                nameof(ATagNestedInsideAnUntaggedPositionIsRefused),
        };

    private const string RunId = "run-2026-08-31-a";

    private const string ModifiedTempBy =
        "http://data.legilux.public.lu/resource/ontology/jolux#modifiedTempBy";

    // ---------- fixtures ----------

    private static FactOccurrence Occurrence(int ordinal = 0) => new(RunId, ordinal);

    private static OfficialIdentity Loi2006 => new(
        "eli",
        "http://data.legilux.public.lu/eli/etat/leg/loi/2006/07/31/n2");

    private static OfficialIdentity Loi2018 => new(
        "eli",
        "http://data.legilux.public.lu/eli/etat/leg/loi/2018/08/10/a703");

    private static RelationTarget HeldTarget => new(Loi2018, IsHeld: true, UnheldReason: null);

    private static PublisherRelation Relation(params QualifiedAxiom[] axioms) => new(
        Loi2006,
        HeldTarget,
        "http://data.legilux.public.lu/resource/ontology/jolux#modifies",
        axioms,
        Occurrence());

    private static PublisherRelation RelationAt(int ordinal, params QualifiedAxiom[] axioms) =>
        Relation(axioms) with { Occurrence = Occurrence(ordinal) };

    private static QualifiedAxiom Axiom(
        string predicate,
        string value,
        int ordinal,
        string? datatype = null,
        string? remote = null) =>
        new(predicate, value, datatype, remote, ordinal);

    private static PublisherDate Date() => new(
        RawLexicalValue: "2024-02-27",
        Datatype: "http://www.w3.org/2001/XMLSchema#date",
        Precision: DatePrecision.Day,
        SourcePredicateUri: "http://data.legilux.public.lu/resource/ontology/jolux#dateApplicability",
        RemoteAxiomUri: "axiom://remote/7",
        RawQualifier: "{EV|http://publications.europa.eu/resource/authority/x}",
        ParsedAuthorityIdentity: "http://publications.europa.eu/resource/authority/x",
        PublisherComment: "entry into force per article 12",
        SemanticRole: "entry_into_force",
        OpenSentinel: OpenSentinelState.Bounded);

    private static PublisherDateFact DateFact() => new(Loi2006, Date(), Occurrence());

    private static DerivedInverseRelation DerivedInverse() => new(
        Loi2018,
        new RelationTarget(Loi2006, IsHeld: true, UnheldReason: null),
        "http://data.legilux.public.lu/resource/ontology/jolux#modifiedBy",
        Relation(Axiom("q#a", "v", 0)),
        "http://data.legilux.public.lu/resource/ontology/jolux#modifies-inverse");

    private static LocalInboundView InboundView() => new(
        Loi2018,
        new[] { Relation(Axiom("q#a", "v", 0)) });

    private static VocabularyDrift Drift() => new("urn:unknown:term", "predicate_uri", Occurrence());

    // ---------- round trip ----------

    [TestMethod]
    public void EveryRelationFieldSurvivesARoundTrip()
    {
        var original = Relation(
            Axiom("q#one", "first", 0, "xsd:string", "axiom://remote/1"),
            Axiom("q#two", "second", 1));

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(original));

        Assert.AreEqual(original, back, "record equality covers every member; a dropped field breaks this");
    }

    [TestMethod]
    public void EveryDateFieldSurvivesARoundTrip()
    {
        var original = Date();

        var back = FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(original));

        Assert.AreEqual(original, back, "all ten date components must survive");
    }

    [TestMethod]
    public void EveryDateFactFieldSurvivesARoundTrip()
    {
        var original = DateFact();

        var back = FactsJson.Deserialize<PublisherDateFact>(FactsJson.Serialize(original));

        Assert.AreEqual(original, back);
        Assert.AreEqual(RunId, back.Occurrence.SourceObservationId,
            "a date that cannot be traced to an observation cannot be checked");
    }

    [TestMethod]
    public void EveryDerivedInverseFieldSurvivesARoundTrip()
    {
        var original = DerivedInverse();

        var back = FactsJson.Deserialize<DerivedInverseRelation>(FactsJson.Serialize(original));

        Assert.AreEqual(original, back);
        Assert.AreEqual(original.DerivedFrom, back.DerivedFrom,
            "the single originating assertion must survive whole, not as an identifier");
    }

    [TestMethod]
    public void EveryLocalInboundViewFieldSurvivesARoundTrip()
    {
        var original = InboundView();

        var back = FactsJson.Deserialize<LocalInboundView>(FactsJson.Serialize(original));

        Assert.AreEqual(original, back);
    }

    [TestMethod]
    public void VocabularyDriftCarriesItsObservationSoItReachesRunAccounting()
    {
        var back = FactsJson.Deserialize<VocabularyDrift>(FactsJson.Serialize(Drift()));

        Assert.AreEqual(Drift(), back);
        Assert.AreEqual(RunId, back.Occurrence.SourceObservationId,
            "drift without its observation cannot be attributed to a run");
    }

    [TestMethod]
    [DynamicData(nameof(UnionVariants))]
    public void EveryUnionVariantKeepsItsExactTypeAcrossARoundTrip(RelationFact fact, string kind)
    {
        var json = FactsJson.Serialize(fact);

        Assert.IsTrue(
            JsonNode.Parse(json)!.AsObject().TryGetPropertyValue("fact_kind", out var tag)
            && tag!.GetValue<string>() == kind,
            "the union writes an explicit tag rather than relying on shape");

        var back = FactsJson.Deserialize<RelationFact>(json);

        Assert.AreEqual(fact.GetType(), back.GetType(), "the variant type must survive the wire");
        Assert.AreEqual(fact, back);
    }

    private static IEnumerable<object[]> UnionVariants =>
    [
        [Relation(Axiom("q#a", "v", 0)), PublisherRelation.Kind],
        [DerivedInverse(), DerivedInverseRelation.Kind],
        [InboundView(), LocalInboundView.Kind],
    ];

    // ---------- the multimap ----------

    [TestMethod]
    public void DuplicateAxiomPredicatesBothSurvive()
    {
        // A publisher may qualify one assertion twice with the same predicate. Collapsing
        // to a map would silently discard the second statement.
        var original = Relation(
            Axiom("q#same", "first value", 0, remote: "axiom://remote/1"),
            Axiom("q#same", "second value", 1, remote: "axiom://remote/2"));

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(original));

        Assert.AreEqual(2, back.Axioms.Count, "both axioms with the same predicate must survive");
        CollectionAssert.AreEqual(
            original.Axioms.Select(a => a.RawLexicalValue).ToList(),
            back.Axioms.Select(a => a.RawLexicalValue).ToList());
    }

    [TestMethod]
    public void DuplicateRemoteAxiomIdentifiersAreNotDeduplicated()
    {
        // Two publishers reusing one axiom URI is not the same axiom. The remote URI is
        // provenance, so identity must not be taken from it.
        var original = Relation(
            Axiom("q#a", "alpha", 0, remote: "axiom://shared"),
            Axiom("q#b", "beta", 1, remote: "axiom://shared"));

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(original));

        Assert.AreEqual(2, back.Axioms.Count, "a shared remote axiom URI must not merge two axioms");
    }

    [TestMethod]
    public void TwoByteIdenticalAxiomsRemainTwoAxioms()
    {
        // Same predicate, same value, same datatype, same remote URI. Only the occurrence
        // ordinal separates them, which is exactly why it exists.
        var original = Relation(
            Axiom("q#same", "same", 0),
            Axiom("q#same", "same", 1));

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(original));

        Assert.AreEqual(2, back.Axioms.Count);
        Assert.AreNotEqual(back.Axioms[0], back.Axioms[1],
            "occurrence identity keeps two identical statements distinguishable");
    }

    [TestMethod]
    public void TwoByteIdenticalPublisherRowsRemainDistinctByOccurrence()
    {
        var first = RelationAt(0, Axiom("q#a", "v", 0));
        var second = RelationAt(1, Axiom("q#a", "v", 0));

        Assert.AreNotEqual(first, second, "a publisher stating the same thing twice stated it twice");

        var deduplicated = new HashSet<PublisherRelation> { first, second };
        Assert.AreEqual(2, deduplicated.Count,
            "even a set must not collapse two occurrences of one statement");
    }

    [TestMethod]
    public void EqualityIsStructuralOverAxiomsNotReferential()
    {
        // The compiler-generated record equality compares the axiom list by REFERENCE.
        // Without the explicit override, two relations carrying identical axioms are
        // unequal, and anything deduplicating or digesting relations is silently wrong.
        var a = Relation(Axiom("q#a", "v", 0));
        var b = Relation(Axiom("q#a", "v", 0));

        Assert.AreEqual(a, b, "identical axioms must compare equal across separate instances");
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "equal values must hash equally");
    }

    [TestMethod]
    public void EqualityDistinguishesAxiomOrderAndDuplicates()
    {
        // Publisher order is itself an observation, and a repeated predicate is a
        // repeated statement, so neither may be normalised away by the comparison.
        var one = Relation(Axiom("q#a", "1", 0), Axiom("q#b", "2", 1));
        var swapped = Relation(Axiom("q#b", "2", 1), Axiom("q#a", "1", 0));
        var doubled = Relation(Axiom("q#a", "1", 0), Axiom("q#a", "1", 1));

        Assert.AreNotEqual(one, swapped, "reordered axioms are not the same observation");
        Assert.AreNotEqual(one, doubled, "a duplicated statement is not the same as one");
    }

    [TestMethod]
    public void EqualityIsStructuralOverAnInboundView()
    {
        var a = InboundView();
        var b = InboundView();

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    // ---------- hostile: provenance ----------

    [TestMethod]
    public void ADerivedInverseWithoutItsOriginIsRefused()
    {
        var json = WithoutMember(FactsJson.Serialize(DerivedInverse()), "derived_from");

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<DerivedInverseRelation>(json));
    }

    [TestMethod]
    public void ADerivedInverseWithNullOriginIsRefused()
    {
        // JsonRequired is satisfied by an explicit null, so presence alone is not enough.
        var node = JsonNode.Parse(FactsJson.Serialize(DerivedInverse()))!.AsObject();
        node["derived_from"] = null;

        AssertFailsClosed<FactsContractViolationException>(() =>
            FactsJson.Deserialize<DerivedInverseRelation>(node.ToJsonString()));
    }

    [TestMethod]
    public void ADerivedInverseWithoutAnAuthorisingOntologyIsRefused()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(DerivedInverse()))!.AsObject();
        node["authorising_ontology_uri"] = string.Empty;

        AssertFailsClosed<FactsContractViolationException>(() =>
            FactsJson.Deserialize<DerivedInverseRelation>(node.ToJsonString()));
    }

    [TestMethod]
    public void AFactWithoutItsObservationIsRefused()
    {
        var json = WithoutMember(FactsJson.Serialize(Relation()), "occurrence");

        AssertFailsClosed<JsonException>(() => FactsJson.Deserialize<PublisherRelation>(json));
    }

    [TestMethod]
    public void AFactWithABlankObservationIdIsRefused()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Relation()))!.AsObject();
        node["occurrence"]!["source_observation_id"] = string.Empty;

        AssertFailsClosed<FactsContractViolationException>(() =>
            FactsJson.Deserialize<PublisherRelation>(node.ToJsonString()));
    }

    [TestMethod]
    public void NoFactMemberNamesAStorageCoordinate()
    {
        // Custody belongs to the observation record. A digest, byte count, container,
        // bucket, URL or locator appearing here would be a second coordinate for one set
        // of bytes, and the two would eventually disagree invisibly.
        string[] forbidden =
        [
            "locator", "url", "uri_locator", "container", "bucket", "blob", "sas",
            "path", "digest", "sha256", "byte_count", "backend", "retention",
        ];

        foreach (var (name, json) in AllSerialisedFacts())
        {
            foreach (var member in MemberNames(json))
            {
                Assert.IsFalse(
                    forbidden.Contains(member, StringComparer.Ordinal),
                    $"{name} carries the storage coordinate '{member}'; custody belongs to the observation record");
            }
        }
    }

    // ---------- hostile: closed vocabulary ----------

    [TestMethod]
    public void AnUnknownUnheldReasonFailsClosedRatherThanDeserialisingSilently()
    {
        var json = """
            {"identity":{"scheme":"eli","value":"x"},"is_held":false,"unheld_reason":"something_new"}
            """;

        AssertFailsClosed<JsonException>(() => FactsJson.Deserialize<RelationTarget>(json));
    }

    [TestMethod]
    public void AnIntegerUnheldReasonFailsClosed()
    {
        // A closed vocabulary that accepts its own ordinal numbers is not closed: any
        // integer becomes a member, including one no publisher ever used.
        var json = """
            {"identity":{"scheme":"eli","value":"x"},"is_held":false,"unheld_reason":2}
            """;

        AssertFailsClosed<JsonException>(() => FactsJson.Deserialize<RelationTarget>(json));
    }

    [TestMethod]
    public void AnUnknownPrecisionFailsClosed()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Date()))!.AsObject();
        node["precision"] = "decade";

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<PublisherDate>(node.ToJsonString()));
    }

    [TestMethod]
    public void AnUnknownOpenSentinelStateFailsClosed()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Date()))!.AsObject();
        node["open_sentinel"] = "probably_open";

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<PublisherDate>(node.ToJsonString()));
    }

    [TestMethod]
    public void UnsupportedVocabularyResolvesToDriftRatherThanADefault()
    {
        var result = FactsVocabulary.ResolveUnheldReason(
            "retired_in_2027", "relation_target.unheld_reason", Occurrence());

        var drift = result.Match(
            accepted: _ => throw new AssertFailedException(
                "an unsupported term must never resolve to a member of the closed vocabulary"),
            drifted: d => d);

        Assert.AreEqual("retired_in_2027", drift.TermUri, "the publisher's own spelling survives");
        Assert.AreEqual("relation_target.unheld_reason", drift.EncounteredIn,
            "drift is attributable to a place, not only to a run");
        Assert.AreEqual(RunId, drift.Occurrence.SourceObservationId);
    }

    [TestMethod]
    public void SupportedVocabularyResolvesToItsExactMember()
    {
        var resolved = FactsVocabulary
            .ResolveUnheldReason("ecli_missing", "relation_target.unheld_reason", Occurrence())
            .Match(accepted: value => value, drifted: _ => throw new AssertFailedException(
                "a supported term must resolve rather than drift"));

        Assert.AreEqual(UnheldTargetReason.EcliMissing, resolved);
    }

    [TestMethod]
    public void VocabularyResolutionIsOrdinalNotCaseInsensitive()
    {
        // A case-insensitive comparison quietly widens a closed vocabulary to accept
        // tokens the publisher did not use.
        var result = FactsVocabulary.ResolveUnheldReason(
            "ECLI_MISSING", "relation_target.unheld_reason", Occurrence());

        Assert.IsInstanceOfType<FactsResult<UnheldTargetReason>.Drifted>(result);
    }

    // ---------- hostile: the closed union ----------

    [TestMethod]
    public void AnUnknownFactKindFailsClosed()
    {
        var node = JsonNode.Parse(FactsJson.Serialize<RelationFact>(Relation()))!.AsObject();
        node["fact_kind"] = "inferred_relation";

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<RelationFact>(node.ToJsonString()));
    }

    [TestMethod]
    public void AMissingFactKindIsRefusedRatherThanGuessedFromShape()
    {
        var json = WithoutMember(FactsJson.Serialize<RelationFact>(Relation()), "fact_kind");

        AssertFailsClosed<JsonException>(() => FactsJson.Deserialize<RelationFact>(json));
    }

    [TestMethod]
    public void AVariantCannotDeserialiseAsAnother()
    {
        // A publisher assertion relabelled as an ontology-authorised inverse must fail,
        // not silently acquire the stronger status.
        var node = JsonNode.Parse(FactsJson.Serialize<RelationFact>(Relation()))!.AsObject();
        node["fact_kind"] = DerivedInverseRelation.Kind;

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<RelationFact>(node.ToJsonString()));
    }

    [TestMethod]
    public void ATaggedDocumentIsRefusedByTheUntaggedVariantReader()
    {
        // The tag is meaningful only to the union. Reading a tagged document straight
        // into a variant must fail rather than skip the tag, because a skipped tag is
        // the same as trusting shape: a derived edge relabelled and then read as an
        // untagged assertion would lose the only thing marking it as derived.
        var tagged = FactsJson.Serialize<RelationFact>(Relation());

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<PublisherRelation>(tagged));
    }

    [TestMethod]
    public void ATagNestedInsideAnUntaggedPositionIsRefused()
    {
        // The same hazard one level down. The refusal must not depend on the tag being
        // at the root, because the origin of a derived inverse is an untagged assertion
        // and a tag smuggled in there would be equally silent.
        var node = JsonNode.Parse(FactsJson.Serialize(DerivedInverse()))!.AsObject();
        node["derived_from"]!["fact_kind"] = PublisherRelation.Kind;

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<DerivedInverseRelation>(node.ToJsonString()));
    }

    // ---------- hostile: document shape ----------

    [TestMethod]
    public void AnUnmappedMemberIsRefused()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Relation()))!.AsObject();
        node["confidence"] = 0.9;

        AssertFailsClosed<JsonException>(() =>
            FactsJson.Deserialize<PublisherRelation>(node.ToJsonString()));
    }

    [TestMethod]
    public void ADuplicateMemberIsRefused()
    {
        // The platform keeps the last occurrence and discards the first without a trace,
        // which is indistinguishable from the publisher never having said it.
        var json = """
            {"scheme":"eli","value":"first","value":"second"}
            """;

        AssertFailsClosed<JsonException>(() => FactsJson.Deserialize<OfficialIdentity>(json));
    }

    [TestMethod]
    public void AnUnheldTargetWithNoReasonIsRefused()
    {
        var json = """
            {"identity":{"scheme":"eli","value":"x"},"is_held":false,"unheld_reason":null}
            """;

        AssertFailsClosed<FactsContractViolationException>(() =>
            FactsJson.Deserialize<RelationTarget>(json));
    }

    [TestMethod]
    public void AHeldTargetCarryingAReasonIsRefused()
    {
        var json = """
            {"identity":{"scheme":"eli","value":"x"},"is_held":true,"unheld_reason":"not_yet_held"}
            """;

        AssertFailsClosed<FactsContractViolationException>(() =>
            FactsJson.Deserialize<RelationTarget>(json));
    }

    // ---------- hostile: dates ----------

    [TestMethod]
    public void AnUnparseableDateIsRetainedRatherThanDiscarded()
    {
        // An unparseable publisher date is a fact about the publisher, not an error.
        var date = Date() with
        {
            RawLexicalValue = "sans date",
            Datatype = null,
            Precision = null,
            RawQualifier = null,
            ParsedAuthorityIdentity = null,
            SemanticRole = null,
        };

        var back = FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(date));

        Assert.AreEqual("sans date", back.RawLexicalValue);
    }

    [TestMethod]
    public void AnUnresolvedQualifierLeavesTheAuthorityNullWithoutLosingTheRawText()
    {
        var date = Date() with
        {
            RawQualifier = "{UNKNOWN|urn:nothing}",
            ParsedAuthorityIdentity = null,
        };

        var back = FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(date));

        Assert.IsNull(back.ParsedAuthorityIdentity, "unresolved means null, not invented");
        Assert.AreEqual("{UNKNOWN|urn:nothing}", back.RawQualifier,
            "the raw qualifier is kept regardless");
    }

    [TestMethod]
    public void AMissingQualifierIsNotFilledInFromTheDatatype()
    {
        var date = Date() with { RawQualifier = null, ParsedAuthorityIdentity = null };

        var back = FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(date));

        Assert.IsNull(back.RawQualifier);
        Assert.IsNull(back.ParsedAuthorityIdentity);
    }

    [TestMethod]
    public void MixedPrecisionIsPreservedRatherThanNormalised()
    {
        var year = Date() with { RawLexicalValue = "2024", Precision = DatePrecision.Year };
        var day = Date() with { RawLexicalValue = "2024-03-05", Precision = DatePrecision.Day };

        Assert.AreEqual(
            DatePrecision.Year,
            FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(year)).Precision,
            "a year-precision date must not become January the first");
        Assert.AreEqual(
            DatePrecision.Day,
            FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(day)).Precision);
    }

    [TestMethod]
    public void AnOpenSentinelIsCarriedAsAStateNotAFarFutureDate()
    {
        var date = Date() with
        {
            RawLexicalValue = "9999-12-31",
            OpenSentinel = OpenSentinelState.OpenEnded,
        };

        var back = FactsJson.Deserialize<PublisherDate>(FactsJson.Serialize(date));

        Assert.AreEqual(OpenSentinelState.OpenEnded, back.OpenSentinel);
        Assert.AreEqual("9999-12-31", back.RawLexicalValue,
            "the publisher's own sentinel text is kept");
    }

    [TestMethod]
    public void TheDateRoleIsNeverInferredFromPosition()
    {
        // Two dates on one subject, in a stated order, neither carrying a role. Position
        // must not supply one: a deadline is not an application date because it appeared
        // second.
        var first = new PublisherDateFact(
            Loi2006,
            Date() with { SemanticRole = null, RawLexicalValue = "2024-01-01" },
            Occurrence(0));
        var second = new PublisherDateFact(
            Loi2006,
            Date() with { SemanticRole = null, RawLexicalValue = "2024-06-30" },
            Occurrence(1));

        foreach (var fact in new[] { first, second })
        {
            var back = FactsJson.Deserialize<PublisherDateFact>(FactsJson.Serialize(fact));
            Assert.IsNull(back.Date.SemanticRole,
                "an unstated role stays unstated whatever position the date occupies");
        }

        // Ordering the pair canonically must not change either role either.
        foreach (var ordered in FactsCanonicalizer.InCanonicalOrder(new[] { second, first }))
        {
            Assert.IsNull(ordered.Date.SemanticRole);
        }
    }

    [TestMethod]
    public void ModifiedTempByCarriesNoInferredInterval()
    {
        // modifiedTempBy is a raw publisher relation. No interval is computed for it from
        // ordinary dates on either related act, and the contract offers nowhere to put
        // one: a member of date type on a relation is exactly how an inferred interval
        // would first appear.
        var relation = Relation() with { PredicateUri = ModifiedTempBy };

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(relation));

        Assert.AreEqual(ModifiedTempBy, back.PredicateUri);
        Assert.AreEqual(0, back.Axioms.Count,
            "an assertion the publisher did not qualify acquires no qualifiers here");

        CollectionAssert.AreEquivalent(
            new[] { "source", "target", "predicate_uri", "axioms", "occurrence" },
            MemberNames(FactsJson.Serialize(relation)).ToList(),
            "a relation carries exactly these members; an interval member would show up here");

        foreach (var property in typeof(PublisherRelation).GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.AreNotEqual(typeof(PublisherDate), property.PropertyType,
                $"PublisherRelation.{property.Name} is a date; a relation that holds a date holds an inferred interval");
        }
    }

    // ---------- unheld targets ----------

    [TestMethod]
    public void ACaseRelationWithoutAnEcliKeepsItsCellarIdentity()
    {
        var target = new RelationTarget(
            new OfficialIdentity("cellar", "cellar:abc-123"),
            IsHeld: false,
            UnheldReason: UnheldTargetReason.EcliMissing);

        var back = FactsJson.Deserialize<RelationTarget>(FactsJson.Serialize(target));

        Assert.AreEqual(UnheldTargetReason.EcliMissing, back.UnheldReason);
        Assert.AreEqual("cellar:abc-123", back.Identity.Value,
            "the edge is neither dropped nor given an invented ECLI");
    }

    [TestMethod]
    public void ATargetOutsideBodyScopeRemainsAnIdentifiedLegalObject()
    {
        var target = new RelationTarget(
            new OfficialIdentity("celex", "32016R0679"),
            IsHeld: false,
            UnheldReason: UnheldTargetReason.OutsideBodyScope);

        var back = FactsJson.Deserialize<RelationTarget>(FactsJson.Serialize(target));

        Assert.IsFalse(back.IsHeld);
        Assert.AreEqual("32016R0679", back.Identity.Value, "identity survives even with no body");
    }

    [TestMethod]
    public void AnUnknownPredicateIsCarriedVerbatimRatherThanRejected()
    {
        // Drift in a predicate URI is data, not a parse error: the URI is a free string
        // so an unrecognised one reaches run accounting instead of vanishing.
        var relation = Relation() with { PredicateUri = "http://example.invalid/ontology#neverSeenBefore" };

        var back = FactsJson.Deserialize<PublisherRelation>(FactsJson.Serialize(relation));

        Assert.AreEqual("http://example.invalid/ontology#neverSeenBefore", back.PredicateUri);
    }

    // ---------- canonical bytes and canonical order ----------

    [TestMethod]
    public void CanonicalBytesCarryTheFrozenIdentity()
    {
        var bytes = FactsCanonicalizer.Canonicalize(Relation(Axiom("q#a", "v", 0)));
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.IsTrue(text.StartsWith(FactsCanonicalizer.Identity + "\n", StringComparison.Ordinal),
            "canonical bytes name the canonicalisation that produced them");
        Assert.AreEqual("lex-v3-facts-canonical-json/1", text.Split('\n')[0],
            "the facts canonicalisation is its own frozen identity, not the preview one");
    }

    [TestMethod]
    public void CanonicalBytesSortObjectMembers()
    {
        var bytes = FactsCanonicalizer.Canonicalize(Occurrence(3));
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        StringAssert.Contains(text, "{\"ordinal\":3,\"source_observation_id\":\"" + RunId + "\"}");
    }

    [TestMethod]
    public void CanonicalBytesNeverSortAnArray()
    {
        // Publisher order is an observation. Sorting it would report our arrangement as
        // the publisher's, and the two documents would wrongly share a digest.
        var forward = Relation(Axiom("q#b", "2", 0), Axiom("q#a", "1", 1));
        var reversed = Relation(Axiom("q#a", "1", 1), Axiom("q#b", "2", 0));

        CollectionAssert.AreNotEqual(
            FactsCanonicalizer.Canonicalize(forward),
            FactsCanonicalizer.Canonicalize(reversed),
            "two different publisher orderings must not canonicalise to the same bytes");
    }

    [TestMethod]
    public void CanonicalBytesCarryTheUnionTag()
    {
        // A relation fact's identity always includes its variant, whatever static type
        // the caller happened to hold it as. Otherwise a publisher assertion and a
        // derived inverse over the same two objects could share a digest, and whichever
        // was stored second would look like a duplicate of the first.
        var asVariant = FactsCanonicalizer.Canonicalize(Relation(Axiom("q#a", "v", 0)));
        var asUnion = FactsCanonicalizer.Canonicalize<RelationFact>(Relation(Axiom("q#a", "v", 0)));

        CollectionAssert.AreEqual(asVariant, asUnion,
            "the static type a caller holds must not change a fact's canonical identity");
        StringAssert.Contains(
            System.Text.Encoding.UTF8.GetString(asVariant),
            "\"fact_kind\":\"publisher_relation\"");
    }

    [TestMethod]
    public void CanonicalBytesSeparateVariantsThatShareTheirMembers()
    {
        var assertion = Relation(Axiom("q#a", "v", 0));
        var view = new LocalInboundView(assertion.Source, new[] { assertion });

        CollectionAssert.AreNotEqual(
            FactsCanonicalizer.Canonicalize(assertion),
            FactsCanonicalizer.Canonicalize(view),
            "an official statement and a locally computed view never share canonical bytes");
    }

    [TestMethod]
    public void CanonicalOrderIsOrdinalOverCompleteEntries()
    {
        var third = RelationAt(2, Axiom("q#a", "v", 0));
        var first = RelationAt(0, Axiom("q#a", "v", 0));
        var second = RelationAt(1, Axiom("q#a", "v", 0));

        var ordered = FactsCanonicalizer.InCanonicalOrder(new[] { third, first, second });

        CollectionAssert.AreEqual(
            new[] { first, second, third }.Select(r => r.Occurrence.Ordinal).ToList(),
            ordered.Select(r => r.Occurrence.Ordinal).ToList(),
            "order comes from the complete canonical entry, occurrence identity included");
    }

    [TestMethod]
    public void CanonicalOrderKeepsEveryEntryIncludingIdenticalOnes()
    {
        var relation = Relation(Axiom("q#a", "v", 0));

        var ordered = FactsCanonicalizer.InCanonicalOrder(new[] { relation, relation, relation });

        Assert.AreEqual(3, ordered.Count,
            "ordering never deduplicates; equal bytes mean the caller supplied the occurrence twice");
    }

    [TestMethod]
    public void CanonicalOrderDoesNotDependOnInputOrder()
    {
        var facts = new[]
        {
            RelationAt(2, Axiom("q#a", "v", 0)),
            RelationAt(0, Axiom("q#a", "v", 0)),
            RelationAt(1, Axiom("q#a", "v", 0)),
        };

        var forward = FactsCanonicalizer.InCanonicalOrder(facts);
        var reversed = FactsCanonicalizer.InCanonicalOrder(facts.Reverse().ToArray());

        CollectionAssert.AreEqual(forward.ToList(), reversed.ToList(),
            "a canonical order that depends on input order is not canonical");
    }

    // ---------- schemas and contracts agree ----------

    [TestMethod]
    [DynamicData(nameof(SchemaAgreements))]
    public void EverySerialisedContractValidatesAgainstItsPublishedSchema(
        string schemaFile,
        string json)
    {
        var result = Schema(schemaFile).Evaluate(ToElement(json), Evaluation());

        Assert.IsTrue(result.IsValid,
            $"the C# contract and {schemaFile} must agree exactly");
    }

    [TestMethod]
    [DynamicData(nameof(SchemaAgreements))]
    public void EverySchemaRequiresExactlyTheMembersTheContractEmits(
        string schemaFile,
        string json)
    {
        // Exact agreement in both directions: a member the contract emits but the schema
        // does not require would be droppable without failing validation, and a member
        // the schema requires but the contract never emits could never validate.
        var evaluated = Schema(schemaFile).Evaluate(ToElement(json), Evaluation());
        Assert.IsTrue(evaluated.IsValid, schemaFile);

        var emitted = MemberNames(json).ToHashSet(StringComparer.Ordinal);
        var required = RequiredMembers(schemaFile);

        CollectionAssert.AreEquivalent(
            required.ToList(),
            emitted.ToList(),
            $"{schemaFile} required members and the emitted members must be the same set");
    }

    private static IEnumerable<object[]> SchemaAgreements =>
    [
        ["publisher-relation.schema.json", FactsJson.Serialize(Relation(Axiom("q#a", "v", 0)))],
        ["derived-inverse-relation.schema.json", FactsJson.Serialize(DerivedInverse())],
        ["local-inbound-view.schema.json", FactsJson.Serialize(InboundView())],
        ["publisher-date.schema.json", FactsJson.Serialize(Date())],
        ["publisher-date-fact.schema.json", FactsJson.Serialize(DateFact())],
        ["vocabulary-drift.schema.json", FactsJson.Serialize(Drift())],
    ];

    [TestMethod]
    [DynamicData(nameof(UnionVariants))]
    public void EveryTaggedVariantValidatesAgainstTheUnionSchema(RelationFact fact, string kind)
    {
        var json = FactsJson.Serialize(fact);
        var result = Schema("relation-fact.schema.json").Evaluate(ToElement(json), Evaluation());

        Assert.IsTrue(result.IsValid, $"the tagged {kind} must validate against the union schema");
    }

    [TestMethod]
    public void TheUnionSchemaRejectsAnUnknownTag()
    {
        var node = JsonNode.Parse(FactsJson.Serialize<RelationFact>(Relation()))!.AsObject();
        node["fact_kind"] = "inferred_relation";

        Assert.IsFalse(
            Schema("relation-fact.schema.json")
                .Evaluate(ToElement(node.ToJsonString()), Evaluation()).IsValid,
            "the union is closed in the schema as well as in the type");
    }

    [TestMethod]
    public void TheUnionSchemaRejectsAMisTaggedVariant()
    {
        var node = JsonNode.Parse(FactsJson.Serialize<RelationFact>(Relation()))!.AsObject();
        node["fact_kind"] = DerivedInverseRelation.Kind;

        Assert.IsFalse(
            Schema("relation-fact.schema.json")
                .Evaluate(ToElement(node.ToJsonString()), Evaluation()).IsValid,
            "a publisher assertion wearing the inverse tag matches no variant");
    }

    [TestMethod]
    public void TheSchemaRejectsAnUnheldTargetWithNoReason()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Relation()))!.AsObject();
        node["target"]!["is_held"] = false;
        node["target"]!["unheld_reason"] = null;

        Assert.IsFalse(
            Schema("publisher-relation.schema.json")
                .Evaluate(ToElement(node.ToJsonString()), Evaluation()).IsValid,
            "an unheld target with no reason is an unexplained absence and must be refused");
    }

    [TestMethod]
    public void TheSchemaRejectsAnAdditionalMember()
    {
        var node = JsonNode.Parse(FactsJson.Serialize(Relation()))!.AsObject();
        node["confidence"] = 0.9;

        Assert.IsFalse(
            Schema("publisher-relation.schema.json")
                .Evaluate(ToElement(node.ToJsonString()), Evaluation()).IsValid,
            "the schema is closed, so an invented member fails there too");
    }

    [TestMethod]
    public void EveryPublishedSchemaFileIsExercisedByATest()
    {
        var published = Directory
            .GetFiles(SchemaDirectory(), "*.schema.json")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var exercised = SchemaAgreements
            .Select(row => (string)row[0])
            .Concat(["relation-fact.schema.json", "facts-common.schema.json"])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEquivalent(published, exercised,
            "a published schema no test evaluates is a schema nothing keeps honest");
    }

    [TestMethod]
    public void EverySchemaTitleNamesTheContractGeneration()
    {
        // The contract line and the published schemas are one versioned family. A schema
        // left behind at an older generation would still validate today's documents and
        // would say nothing about it.
        foreach (var path in Directory.GetFiles(SchemaDirectory(), "*.schema.json"))
        {
            var title = JsonNode.Parse(File.ReadAllText(path))!["title"]!.GetValue<string>();
            StringAssert.StartsWith(title, FactsContractLine.Generation,
                $"{Path.GetFileName(path)} does not name the contract generation");
        }
    }

    [TestMethod]
    public void EverySchemaFileNameMatchesTheV3StructuralBoundary()
    {
        // The integration path verifier admits one-level lowercase V3 schema families
        // only. A file this package publishes that the verifier refuses would block the
        // pull request rather than this test.
        foreach (var path in Directory.GetFiles(SchemaDirectory(), "*.json"))
        {
            var name = Path.GetFileName(path);
            Assert.IsTrue(
                System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9-]+\\.schema\\.json$"),
                $"{name} is outside the bounded schema-family pattern");
        }
    }

    // ---------- the receipts themselves ----------

    [TestMethod]
    public void EveryDeclaredMutationNamesATestThatExists()
    {
        var methods = typeof(PublisherFactsTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<TestMethodAttribute>() is not null)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (mutation, testName) in MutationReceipts)
        {
            Assert.IsTrue(methods.Contains(testName),
                $"the receipt for '{mutation}' names {testName}, which is not a test in this class");
        }
    }

    // ---------- helpers ----------

    private static void AssertFailsClosed<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (Exception thrown)
        {
            for (var current = thrown; current is not null; current = current.InnerException)
            {
                if (current is TException)
                {
                    return;
                }
            }

            throw new AssertFailedException(
                $"expected {typeof(TException).Name} somewhere in the chain, got {thrown.GetType().Name}: {thrown.Message}");
        }

        throw new AssertFailedException(
            $"expected {typeof(TException).Name}; the hostile document was accepted instead");
    }

    private static string WithoutMember(string json, string member)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.IsTrue(node.Remove(member), $"the fixture must actually contain '{member}'");
        return node.ToJsonString();
    }

    private static IEnumerable<string> MemberNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            yield return property.Name;
        }
    }

    private static IEnumerable<(string Name, string Json)> AllSerialisedFacts()
    {
        yield return (nameof(PublisherRelation), FactsJson.Serialize(Relation(Axiom("q#a", "v", 0))));
        yield return (nameof(DerivedInverseRelation), FactsJson.Serialize(DerivedInverse()));
        yield return (nameof(LocalInboundView), FactsJson.Serialize(InboundView()));
        yield return (nameof(PublisherDate), FactsJson.Serialize(Date()));
        yield return (nameof(PublisherDateFact), FactsJson.Serialize(DateFact()));
        yield return (nameof(VocabularyDrift), FactsJson.Serialize(Drift()));
        yield return (nameof(FactOccurrence), FactsJson.Serialize(Occurrence()));
        yield return (nameof(RelationTarget), FactsJson.Serialize(HeldTarget));
    }

    private static readonly Lazy<IReadOnlyDictionary<string, JsonSchema>> LoadedSchemas =
        new(LoadSchemas);

    private static IReadOnlyDictionary<string, JsonSchema> LoadSchemas()
    {
        // One private registry holding every published file, because the graph refers
        // across files by absolute $id and an unresolved reference would silently
        // evaluate as valid.
        var registry = new SchemaRegistry();
        var options = new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry };
        var loaded = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);

        foreach (var path in Directory
                     .GetFiles(SchemaDirectory(), "*.schema.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(path), options);
            registry.Register(schema);
            loaded[Path.GetFileName(path)] = schema;
        }

        return loaded;
    }

    private static JsonSchema Schema(string fileName) => LoadedSchemas.Value[fileName];

    private static EvaluationOptions Evaluation() => new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    private static JsonElement ToElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static readonly IReadOnlyDictionary<string, string> BodyDefinitions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["publisher-relation.schema.json"] = "publisher_relation_body",
            ["derived-inverse-relation.schema.json"] = "derived_inverse_relation_body",
            ["local-inbound-view.schema.json"] = "local_inbound_view_body",
            ["publisher-date.schema.json"] = "publisher_date_body",
            ["publisher-date-fact.schema.json"] = "publisher_date_fact_body",
            ["vocabulary-drift.schema.json"] = "vocabulary_drift_body",
        };

    private static IEnumerable<string> RequiredMembers(string schemaFile)
    {
        var common = JsonNode
            .Parse(File.ReadAllText(Path.Combine(SchemaDirectory(), "facts-common.schema.json")))!
            .AsObject();

        var body = common["$defs"]![BodyDefinitions[schemaFile]]!.AsObject();
        return body["required"]!.AsArray().Select(entry => entry!.GetValue<string>());
    }

    private static string SchemaDirectory() =>
        Path.Combine(RepositoryRoot(), "schemas", "v3-facts");

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "schemas")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        return directory ?? throw new InvalidOperationException("repository root not found");
    }
}
