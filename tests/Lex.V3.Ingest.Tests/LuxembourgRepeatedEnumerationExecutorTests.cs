using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgRepeatedEnumerationExecutorTests
{
    private const string IntegerDatatype = "http://www.w3.org/2001/XMLSchema#integer";
    private const string LanguageStringDatatype =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    [TestMethod]
    public void BoundCountOpensTheRetainedRendererForOfflineVerification()
    {
        var fixture = CreateFixture();
        var bound = fixture.Plan.BindCount(
            ResourceId(10),
            ResourceId(11),
            ResourceId(12),
            fixture.SetId,
            LuxembourgQueryPass.Pass1,
            Partition(),
            Artifact(13));

        var renderer = bound.OpenEvidenceRenderer();

        Assert.AreSame(renderer, bound.OpenEvidenceRenderer());
        MachineQueryBinder.VerifyOffline(
            bound.MachinePlan,
            bound.MachinePlanRef,
            bound.InputArtifact,
            bound.Request.RenderReceipt,
            renderer);
    }

    [TestMethod]
    public void BoundPageOpensTheRetainedRendererForOfflineVerification()
    {
        var fixture = CreateFixture();
        var bound = fixture.Plan.BindPage(
            ResourceId(20),
            ResourceId(21),
            ResourceId(22),
            fixture.SetId,
            LuxembourgQueryPass.Pass2,
            Partition(),
            lastCursor: null,
            expectedPartitionRowCount: 2,
            expectedPartitionRowCountEvidenceRef: Artifact(23),
            rendererSourceRef: Artifact(24));

        var renderer = bound.OpenEvidenceRenderer();

        Assert.AreSame(renderer, bound.OpenEvidenceRenderer());
        MachineQueryBinder.VerifyOffline(
            bound.MachinePlan,
            bound.MachinePlanRef,
            bound.InputArtifact,
            bound.Request.RenderReceipt,
            renderer);
    }

    [TestMethod]
    public void ReadCountParsesTheStrictLuxembourgIntegerBinding()
    {
        var fixture = CreateFixture();

        var count = RepeatedEnumerationTraversalReader.ReadCount(
            CountBytes(fixture.Profile, "typed-literal"),
            fixture.Profile);

        Assert.AreEqual(42L, count);
    }

    [TestMethod]
    public void ReadCountRejectsTheWrongLuxembourgLiteralWireType()
    {
        var fixture = CreateFixture();

        Assert.ThrowsExactly<ArgumentException>(() =>
            RepeatedEnumerationTraversalReader.ReadCount(
                CountBytes(fixture.Profile, "literal"),
                fixture.Profile));
    }

    [TestMethod]
    public void ReadPageReturnsTheRowCountAndSixPartFinalCursor()
    {
        var fixture = CreateFixture();
        var first = AssertionRow(fixture, "a", "Alpha");
        var second = AssertionRow(fixture, "b", "Beta");

        var result = RepeatedEnumerationTraversalReader.ReadPage(
            PageBytes(fixture.Profile, first, second),
            fixture.Profile,
            rowLimit: 2L);

        Assert.AreEqual(2L, result.RowCount);
        CollectionAssert.AreEqual(
            new[]
            {
                Subject("a"),
                fixture.Plan.SelectorPredicates[0],
                "literal",
                "Alpha",
                LanguageStringDatatype,
                "fr",
            },
            result.FirstCursorParts.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                Subject("b"),
                fixture.Plan.SelectorPredicates[0],
                "literal",
                "Beta",
                LanguageStringDatatype,
                "fr",
            },
            result.FinalCursorParts.ToArray());
    }

    [TestMethod]
    public void ReadPageReturnsNoCursorForAnInitialEmptyPage()
    {
        var fixture = CreateFixture();

        var result = RepeatedEnumerationTraversalReader.ReadPage(
            PageBytes(fixture.Profile),
            fixture.Profile,
            rowLimit: 2L);

        Assert.AreEqual(0L, result.RowCount);
        CollectionAssert.AreEqual(Array.Empty<string>(), result.FirstCursorParts.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), result.FinalCursorParts.ToArray());
    }

    [TestMethod]
    public void ReadPageRejectsDuplicateCanonicalKeys()
    {
        var fixture = CreateFixture();
        var duplicate = AssertionRow(fixture, "a", "Alpha");

        Assert.ThrowsExactly<ArgumentException>(() =>
            RepeatedEnumerationTraversalReader.ReadPage(
                PageBytes(fixture.Profile, duplicate, duplicate),
                fixture.Profile,
                rowLimit: 2L));
    }

    [TestMethod]
    public void ReadPageRejectsNonIncreasingCursors()
    {
        var fixture = CreateFixture();

        Assert.ThrowsExactly<ArgumentException>(() =>
            RepeatedEnumerationTraversalReader.ReadPage(
                PageBytes(
                    fixture.Profile,
                    AssertionRow(fixture, "b", "Beta"),
                    AssertionRow(fixture, "a", "Alpha")),
                fixture.Profile,
                rowLimit: 2L));
    }

    [TestMethod]
    [DataRow("datatype")]
    [DataRow("language")]
    [DataRow("iri")]
    public void ReadPageRejectsQualifiedOrNonPlainCursorTerms(string invalidKind)
    {
        var fixture = CreateFixture();
        var row = AssertionRow(fixture, "a", "Alpha");
        row[fixture.Profile.CursorVariables[5]] = invalidKind switch
        {
            "datatype" => TypedLiteral("fr", LanguageStringDatatype),
            "language" => LanguageLiteral("fr", "fr"),
            "iri" => Iri("http://example.test/not-a-plain-cursor"),
            _ => throw new InvalidOperationException("Unknown test case."),
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            RepeatedEnumerationTraversalReader.ReadPage(
                PageBytes(fixture.Profile, row),
                fixture.Profile,
                rowLimit: 1L));
    }

    [TestMethod]
    public void ReadPageRejectsAResponseBeyondTheRowLimit()
    {
        var fixture = CreateFixture();

        Assert.ThrowsExactly<ArgumentException>(() =>
            RepeatedEnumerationTraversalReader.ReadPage(
                PageBytes(
                    fixture.Profile,
                    AssertionRow(fixture, "a", "Alpha"),
                    AssertionRow(fixture, "b", "Beta")),
                fixture.Profile,
                rowLimit: 1L));
    }

    private static (
        LuxembourgQueryPlan Plan,
        string SetId,
        RepeatedEnumerationInterpretationProfile Profile) CreateFixture()
    {
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(Artifact(1), Artifact(2));
        var setId = plan.SetDefinitions.Single(static definition =>
            string.Equals(definition.TemplateId, "assertion-rows", StringComparison.Ordinal)).SetId;
        return (plan, setId, plan.CreateDeliveryProfile(ResourceId(3), setId));
    }

    private static LuxembourgQueryPartitionRange Partition() => new(
        "all",
        new LuxembourgQueryCursor(string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty),
        new LuxembourgQueryCursor("z", string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty));

    private static Dictionary<string, object> AssertionRow(
        (LuxembourgQueryPlan Plan, string SetId, RepeatedEnumerationInterpretationProfile Profile) fixture,
        string subjectSuffix,
        string lexicalValue)
    {
        var subject = Subject(subjectSuffix);
        var predicate = fixture.Plan.SelectorPredicates[0];
        var terms = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["subject"] = Iri(subject),
            ["predicate"] = Iri(predicate),
            ["object"] = LanguageLiteral(lexicalValue, "fr"),
            ["object_kind"] = Literal("literal"),
            ["datatype_iri"] = Literal(LanguageStringDatatype),
            ["language_tag"] = Literal("fr"),
            ["key_1"] = Literal(subject),
            ["key_2"] = Literal(predicate),
            ["key_3"] = Literal("literal"),
            ["key_4"] = Literal(lexicalValue),
            ["key_5"] = Literal(LanguageStringDatatype),
            ["key_6"] = Literal("fr"),
        };
        return fixture.Profile.ProjectionVariables.ToDictionary(
            static variable => variable,
            variable => terms[variable],
            StringComparer.Ordinal);
    }

    private static byte[] CountBytes(
        RepeatedEnumerationInterpretationProfile profile,
        string wireType) => SparqlResultBytes(
        new[] { profile.CountVariable },
        new[]
        {
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [profile.CountVariable] = TypedLiteral("42", IntegerDatatype, wireType),
            },
        });

    private static byte[] PageBytes(
        RepeatedEnumerationInterpretationProfile profile,
        params Dictionary<string, object>[] rows) =>
        SparqlResultBytes(profile.ProjectionVariables, rows);

    private static byte[] SparqlResultBytes(
        IReadOnlyList<string> variables,
        IReadOnlyList<Dictionary<string, object>> rows) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            head = new
            {
                link = Array.Empty<object>(),
                vars = variables,
            },
            results = new
            {
                distinct = false,
                ordered = true,
                bindings = rows,
            },
        });

    private static Dictionary<string, string> Literal(string value) => new(StringComparer.Ordinal)
    {
        ["type"] = "literal",
        ["value"] = value,
    };

    private static Dictionary<string, string> LanguageLiteral(string value, string language) =>
        new(StringComparer.Ordinal)
        {
            ["type"] = "literal",
            ["value"] = value,
            ["xml:lang"] = language,
        };

    private static Dictionary<string, string> TypedLiteral(
        string value,
        string datatype,
        string wireType = "typed-literal") =>
        new(StringComparer.Ordinal)
        {
            ["type"] = wireType,
            ["value"] = value,
            ["datatype"] = datatype,
        };

    private static Dictionary<string, string> Iri(string value) => new(StringComparer.Ordinal)
    {
        ["type"] = "uri",
        ["value"] = value,
    };

    private static string Subject(string suffix) =>
        $"http://data.legilux.public.lu/resource/test/{suffix}";

    private static SourceArtifactRef Artifact(int identity) => new(
        ResourceId(identity),
        new string("0123456789abcdef"[identity % 16], 64));

    private static string ResourceId(int identity) =>
        $"urn:uuid:00000000-0000-0000-0000-{identity:D12}";
}
