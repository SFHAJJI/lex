using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexManifestationEnumerationProducerTests
{
    private const string Work =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string ExpressionA = Work + ".0024";
    private const string ExpressionB = Work + ".0001";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    [TestMethod]
    public void ACompleteExpressionAnswerCarriesEveryTypeAndFindsFormexExactly()
    {
        var expression = Expression(ExpressionA);
        var result = Decode(expression, Row(expression, "fmx4", 2), Row(expression, "html", 1));
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsTrue(result.IsFormexEligible);
        Assert.HasCount(2, result.ManifestationTypes!);
        Assert.AreEqual("fmx4", result.ManifestationTypes![0].PublisherType);
        Assert.AreEqual(2, result.ManifestationTypes[0].Multiplicity);
        Assert.AreSame(expression, result.Expression);
        Assert.IsNotNull(result.Proof);
    }

    [TestMethod]
    public void AnEmptyCompletedAnswerIsIneligibleWithoutClaimingTheExpressionWasNotAsked()
    {
        var result = Decode(Expression(ExpressionA));
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.IsFalse(result.IsFormexEligible);
        Assert.IsEmpty(result.ManifestationTypes!);
        Assert.IsNotNull(result.Proof);
    }

    [TestMethod]
    public void ARowNamingAnotherExpressionRefusesTheWholeAnswer()
    {
        var selected = Expression(ExpressionA);
        var result = Decode(selected, Row(Expression(ExpressionB), "fmx4", 1));
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.RowNamesAnotherExpression, result.Refusal);
        Assert.IsNull(result.ManifestationTypes);
    }

    [TestMethod]
    public void ATypeMarkerKeyOrMultiplicityDisagreementRefuses()
    {
        var expression = Expression(ExpressionA);
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.RowNotAdmitted,
            Decode(expression, Row(expression, "fmx4", 1, marker: "iri")).Refusal);
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.RowNotAdmitted,
            Decode(expression, Row(expression, "fmx4", 1, key2: "html")).Refusal);
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.RowNotAdmitted,
            Decode(expression, Row(expression, "fmx4", 0)).Refusal);
    }

    [TestMethod]
    public void AGroupedTypeDeliveredTwiceRefusesRatherThanDeduplicating()
    {
        var expression = Expression(ExpressionA);
        var result = Decode(expression, Row(expression, "fmx4", 1), Row(expression, "fmx4", 1));
        Assert.AreEqual(
            EuFormexManifestationEnumerationRefusal.ManifestationTypeDeliveredTwice, result.Refusal);
    }

    [TestMethod]
    public void TheDecoderWillNotPairRowsWithAnotherExpressionsProof()
    {
        var expression = Expression(ExpressionA);
        var row = Row(expression, "fmx4", 1);
        var proof = AbsenceFixtures.Delivery(
            EuFormexManifestationDiscoveryPlan.PartitionKeyFor(
                new LanguageScopedExpressionIdentity(Work, ExpressionB)), 1).Proof;
        var result = EuFormexManifestationEnumerationProducer.DecodeRows(
            [row], EuFormexManifestationDiscoveryPlan.Create().CreateDeliveryProfile(), proof,
            expression, LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());
        Assert.AreEqual(EuFormexManifestationEnumerationRefusal.EnumerationProofRefused, result.Refusal);
    }

    [TestMethod]
    public void EligibilityPopulationRequiresExactlyOneCompletedAnswerPerDerivedExpression()
    {
        var first = Expression(ExpressionA);
        var second = Expression(ExpressionB);
        var production = Production(first, second);
        var firstAnswer = Decode(first, Row(first, "fmx4", 1));
        var secondAnswer = Decode(second, Row(second, "html", 1));

        Assert.IsNull(EuFormexEligibilityPopulation.TryCreate(
            production, [firstAnswer], out var missing, out _));
        Assert.AreEqual(EuFormexEligibilityPopulationRefusal.ExpressionEnumerationMissing, missing);

        Assert.IsNull(EuFormexEligibilityPopulation.TryCreate(
            production, [firstAnswer, firstAnswer, secondAnswer], out var duplicate, out _));
        Assert.AreEqual(EuFormexEligibilityPopulationRefusal.ExpressionEnumeratedTwice, duplicate);

        var complete = EuFormexEligibilityPopulation.TryCreate(
            production, [secondAnswer, firstAnswer], out var refusal, out var detail);
        Assert.IsNotNull(complete, $"{refusal}: {detail}");
        Assert.HasCount(2, complete.Enumerations);
        Assert.AreSame(firstAnswer, complete.Enumerations[0], "ordered by the proven expression population.");
        Assert.HasCount(1, complete.EligibleExpressions);
        Assert.AreSame(first, complete.EligibleExpressions[0]);
    }

    [TestMethod]
    public void ARefusedEnumerationCannotSilentlyMakeAnExpressionIneligible()
    {
        var first = Expression(ExpressionA);
        var second = Expression(ExpressionB);
        var production = Production(first, second);
        var firstAnswer = Decode(first, Row(first, "fmx4", 1));
        var refused = EuFormexManifestationEnumerationResult.Refused(
            second,
            EuFormexManifestationEnumerationRefusal.EnumerationRefused,
            "the expression was not enumerated",
            0,
            LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());

        Assert.IsNull(EuFormexEligibilityPopulation.TryCreate(
            production, [firstAnswer, refused], out var refusal, out var detail));
        Assert.AreEqual(EuFormexEligibilityPopulationRefusal.ExpressionEnumerationRefused, refusal);
        StringAssert.Contains(detail, ExpressionB);
    }

    [TestMethod]
    public void AnEnumerationOutsideTheProvenExpressionPopulationRefuses()
    {
        var first = Expression(ExpressionA);
        var foreign = Expression(ExpressionB);
        var production = Production(first);
        var foreignAnswer = Decode(foreign, Row(foreign, "fmx4", 1));

        Assert.IsNull(EuFormexEligibilityPopulation.TryCreate(
            production, [foreignAnswer], out var refusal, out var detail));
        Assert.AreEqual(EuFormexEligibilityPopulationRefusal.ExpressionOutsidePopulation, refusal);
        Assert.AreEqual(ExpressionB, detail);
    }

    [TestMethod]
    public void TheSameExpressionIdentityWithDifferentCanonicalContentRefuses()
    {
        var expected = Expression(ExpressionA);
        var changedContent = Expression(
            ExpressionA,
            "http://publications.europa.eu/resource/authority/language/FRA");
        Assert.AreEqual(expected.Identity, changedContent.Identity);
        Assert.AreNotEqual(expected.CanonicalContentSha256, changedContent.CanonicalContentSha256);
        var production = Production(expected);
        var changedAnswer = Decode(changedContent, Row(changedContent, "fmx4", 1));

        Assert.IsNull(EuFormexEligibilityPopulation.TryCreate(
            production, [changedAnswer], out var refusal, out var detail));
        Assert.AreEqual(EuFormexEligibilityPopulationRefusal.ExpressionContentDisagrees, refusal);
        Assert.AreEqual(ExpressionA, detail);
    }

    [TestMethod]
    public async Task TheOfflineScriptExercisesBothPassesPagingProofAndBudgetEndToEnd()
    {
        var expression = Expression(ExpressionA);
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var page = EuAcquisitionTestFixture.RowsJson(
            plan.CreateDeliveryProfile().ProjectionVariables,
            [JsonRow(expression, "fmx4")]);
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
            {
                ["M"] = new("M",
                    [EuAcquisitionTestFixture.EuCountJson(1), page,
                        EuAcquisitionTestFixture.EuCountJson(1), page]),
            });
        var budget = WireRequestBudget.OfWireRequests(20);
        var producer = new EuFormexManifestationEnumerationProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            handler);

        var result = await producer.RunAsync(
            new EuFormexManifestationRunRequest(
                plan, expression, "urn:uuid:5c30aa43-66fb-4a16-9e25-c5c02558d337",
                EuAcquisitionTestFixture.BuildRendererSource(9811), budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.IsTrue(result.Delivered, $"{result.Refusal}: {result.Detail}");
        Assert.IsTrue(result.IsFormexEligible);
        Assert.AreEqual(4, result.ProductRequestCount, "two count/page passes, robots excluded.");
        Assert.AreEqual(6, result.WireBudget.Spent, "the same ceiling also counts both robots hops.");
        Assert.IsNotNull(result.Proof);
        Assert.AreEqual(EuFormexManifestationDiscoveryPlan.PartitionKeyFor(expression.Identity),
            result.Proof!.FamilyKey);
        CollectionAssert.AreEqual(
            new[] { "Robots", "Robots", "M", "M", "M", "M" },
            handler.FamilySequence.ToArray());
    }

    private static EuFormexManifestationEnumerationResult Decode(
        LanguageScopedExpression expression, params RepeatedEnumerationRow[] rows) =>
        EuFormexManifestationEnumerationProducer.DecodeRows(
            rows,
            EuFormexManifestationDiscoveryPlan.Create().CreateDeliveryProfile(),
            AbsenceFixtures.Delivery(
                EuFormexManifestationDiscoveryPlan.PartitionKeyFor(expression.Identity), rows.Length).Proof,
            expression,
            LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());

    private static RepeatedEnumerationRow Row(
        LanguageScopedExpression expression,
        string type,
        long multiplicity,
        string marker = "literal",
        string? key2 = null)
    {
        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            RepeatedEnumerationRdfTerm.Iri(expression.Identity.PublisherWorkId),
            RepeatedEnumerationRdfTerm.Iri(expression.Identity.PublisherExpressionId),
            RepeatedEnumerationRdfTerm.Literal(type, XsdString, null),
            RepeatedEnumerationRdfTerm.Literal(marker, null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(multiplicity.ToString(), XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            RepeatedEnumerationRdfTerm.Literal(key2 ?? type, null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        };
        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static string JsonRow(LanguageScopedExpression expression, string type)
    {
        static string Term(string kind, string value, string? datatype = null) =>
            "{\"type\":" + JsonSerializer.Serialize(kind) + ",\"value\":"
            + JsonSerializer.Serialize(value)
            + (datatype is null ? string.Empty : ",\"datatype\":" + JsonSerializer.Serialize(datatype)) + "}";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["work"] = Term("uri", expression.Identity.PublisherWorkId),
            ["expression"] = Term("uri", expression.Identity.PublisherExpressionId),
            ["manifestation_type"] = Term("literal", type, XsdString),
            ["manifestation_type_kind"] = Term("literal", "literal"),
            ["datatype_iri"] = Term("literal", XsdString),
            ["language_tag"] = Term("literal", string.Empty),
            ["multiplicity"] = Term("literal", "1", XsdInteger),
            ["key_1"] = Term("literal", "literal"),
            ["key_2"] = Term("literal", type),
            ["key_3"] = Term("literal", XsdString),
            ["key_4"] = Term("literal", string.Empty),
        };
        return "{" + string.Join(',', values.Select(value =>
            JsonSerializer.Serialize(value.Key) + ":" + value.Value)) + "}";
    }

    private static LanguageScopedExpression Expression(
        string expressionIri,
        string officialLanguage = "http://publications.europa.eu/resource/authority/language/ENG") =>
        LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(Work, expressionIri),
            officialLanguage,
            null,
            new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Cellar,
                new SourceRegistryMemberRef(Artifact('b'), "expression"),
                expressionIri,
                "eu-language-scoped-expression:" + expressionIri,
                Sha256("eu-language-scoped-expression:" + expressionIri),
                Artifact('c'),
                null),
            LanguageScopedExpressionLineage.FromContributions(
            [
                new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt()),
            ]));

    private static EuLanguageScopedExpressionProductionResult Production(
        params LanguageScopedExpression[] expressions)
    {
        var proof = AbsenceFixtures.Delivery("expression-facts", expressions.Length).Proof;
        var constructor = typeof(EuLanguageScopedExpressionDerivation).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(AbsenceFamilyEnumerationProof), typeof(AbsenceFamilyEnumerationProof),
                typeof(IReadOnlyList<LanguageScopedExpression>), typeof(byte[]), typeof(byte[])],
            modifiers: null)!;
        var derivation = (EuLanguageScopedExpressionDerivation)constructor.Invoke(
            [proof, null, expressions, Encoding.UTF8.GetBytes("derivation"), Encoding.UTF8.GetBytes("episode")]);
        return EuLanguageScopedExpressionProductionResult.Success(
            derivation, null!, null!, new HashSet<string>([Work], StringComparer.Ordinal), 0);
    }

    private static DurableBlobWriteReceipt Receipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('d', 64), 1, CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse("2026-09-14T00:00:00Z"),
                null));
    }

    private static SourceArtifactRef Artifact(char fill) => new(
        $"urn:uuid:{new string(fill, 8)}-{new string(fill, 4)}-4{new string(fill, 3)}-8{new string(fill, 3)}-{new string(fill, 12)}",
        new string(fill, 64));
    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
