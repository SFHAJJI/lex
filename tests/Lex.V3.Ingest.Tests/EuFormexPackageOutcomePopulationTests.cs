using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexPackageOutcomePopulationTests
{
    private const string Work =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string First = Work + ".0024";
    private const string Second = Work + ".0001";

    [TestMethod]
    public void EveryEligibleExpressionReceivesExactlyOneTypedOutcomeInPopulationOrder()
    {
        var first = Expression(First);
        var second = Expression(Second);
        var eligibility = Eligibility(first, second);
        var refused = EuFormexPackageOutcome.Refused(
            first, EuDocumentFetchAttemptRefusal.WireBudgetExhausted, "ceiling spent");
        var unavailable = EuFormexPackageOutcome.Unavailable(
            second, EuDocumentFetchRefusal.RequestedRepresentationNotServed, 404);

        var population = EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [unavailable, refused], out var refusal, out var detail);

        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.None, refusal, detail);
        Assert.IsNotNull(population);
        Assert.HasCount(2, population.Outcomes);
        Assert.AreSame(refused, population.Outcomes[0]);
        Assert.AreSame(unavailable, population.Outcomes[1]);
        Assert.AreEqual(0, population.AcquiredCount);
        Assert.AreEqual(1, population.UnavailableCount);
        Assert.AreEqual(1, population.RefusedCount);
    }

    [TestMethod]
    public void MissingDuplicateAndIneligibleOutcomesRefuseClosure()
    {
        var first = Expression(First);
        var second = Expression(Second);
        var eligibility = Eligibility(first, second);
        var firstOutcome = EuFormexPackageOutcome.Refused(
            first, EuDocumentFetchAttemptRefusal.ObservationNotExecuted, "offline fixture");

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [firstOutcome], out var missing, out _));
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.OutcomeMissing, missing);

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [firstOutcome, firstOutcome], out var duplicate, out _));
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.ExpressionDisposedTwice, duplicate);

        var foreign = Expression(Work + ".9999");
        var foreignOutcome = EuFormexPackageOutcome.Refused(
            foreign, EuDocumentFetchAttemptRefusal.ObservationNotExecuted, "foreign");
        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [firstOutcome, foreignOutcome], out var outside, out _));
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.OutcomeOutsideExpressionPopulation, outside);
    }

    [TestMethod]
    public void SameIdentityWithDifferentExpressionContentCannotStandInForTheEligibleExpression()
    {
        var expected = Expression(First);
        var changed = Expression(
            First, "http://publications.europa.eu/resource/authority/language/FRA");
        var eligibility = Eligibility(expected);
        var outcome = EuFormexPackageOutcome.Refused(
            changed, EuDocumentFetchAttemptRefusal.ObservationNotExecuted, "changed content");

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [outcome], out var refusal, out var detail));
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.ExpressionContentDisagrees, refusal);
        Assert.AreEqual(First, detail);
    }

    [TestMethod]
    public void TheWholeEnumeratedPopulationRequiresAnExplicitEligibilityDisposition()
    {
        var eligible = Expression(First);
        var ineligible = Expression(Second);
        var eligibility = EligibilityWithIneligible(eligible, ineligible);
        var acquiredRefusal = EuFormexPackageOutcome.Refused(
            eligible, EuDocumentFetchAttemptRefusal.ObservationNotExecuted, "offline fixture");

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [acquiredRefusal], out var missing, out var missingDetail));
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.OutcomeMissing, missing);
        Assert.AreEqual(Second, missingDetail,
            "the expectation is every enumerated expression, not the filtered eligible subset.");

        var explicitIneligible = EuFormexPackageOutcome.NotEligible(ineligible);
        var complete = EuFormexPackageOutcomePopulation.TryClose(
            eligibility, [explicitIneligible, acquiredRefusal], out var refusal, out var detail);
        Assert.AreEqual(EuFormexPackageOutcomePopulationRefusal.None, refusal, detail);
        Assert.IsNotNull(complete);
        Assert.AreEqual(1, complete.NotEligibleCount);
        Assert.AreSame(explicitIneligible, complete.Outcomes[1]);

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility,
            [acquiredRefusal, EuFormexPackageOutcome.Unavailable(
                ineligible, EuDocumentFetchRefusal.RequestedRepresentationNotServed, 404)],
            out var ineligibleHasOutcome,
            out _));
        Assert.AreEqual(
            EuFormexPackageOutcomePopulationRefusal.IneligibleExpressionHasPackageOutcome,
            ineligibleHasOutcome);

        Assert.IsNull(EuFormexPackageOutcomePopulation.TryClose(
            eligibility,
            [EuFormexPackageOutcome.NotEligible(eligible), explicitIneligible],
            out var eligibleMarkedIneligible,
            out _));
        Assert.AreEqual(
            EuFormexPackageOutcomePopulationRefusal.EligibleExpressionMarkedIneligible,
            eligibleMarkedIneligible);
    }

    [TestMethod]
    public void OutcomePayloadShapesAreClosed()
    {
        var expression = Expression(First);
        var unavailable = EuFormexPackageOutcome.Unavailable(
            expression, EuDocumentFetchRefusal.RequestedRepresentationNotServed, 404);
        var refused = EuFormexPackageOutcome.Refused(
            expression, EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused, "robots");

        Assert.AreEqual(EuFormexPackageOutcomeKind.Unavailable, unavailable.Kind);
        Assert.AreEqual(EuDocumentFetchRefusal.RequestedRepresentationNotServed, unavailable.UnavailableReason);
        Assert.AreEqual(404, unavailable.ObservedStatus);
        Assert.IsNull(unavailable.AcquiredInventory);
        Assert.IsNull(unavailable.AcquisitionRefusal);

        Assert.AreEqual(EuFormexPackageOutcomeKind.Refused, refused.Kind);
        Assert.AreEqual(EuDocumentFetchAttemptRefusal.RobotsBootstrapRefused, refused.AcquisitionRefusal);
        Assert.IsNull(refused.UnavailableReason);
        Assert.IsNull(refused.ObservedStatus);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            EuFormexPackageOutcome.Unavailable(
                expression, EuDocumentFetchRefusal.WrongAcceptToken, 400));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            EuFormexPackageOutcome.Refused(
                expression, EuDocumentFetchAttemptRefusal.None, "none"));
    }

    [TestMethod]
    public async Task AcquiredMeansTheInventoryBelongsToTheExactEligibleExpression()
    {
        var expression = Expression(First);
        var inventory = await InventoryAsync(".0024");

        var acquired = EuFormexPackageOutcome.Acquired(expression, inventory);

        Assert.AreEqual(EuFormexPackageOutcomeKind.Acquired, acquired.Kind);
        Assert.AreSame(inventory, acquired.AcquiredInventory);
        Assert.IsNull(acquired.UnavailableReason);
        Assert.IsNull(acquired.AcquisitionRefusal);
        var otherInventory = await InventoryAsync(".0001");
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuFormexPackageOutcome.Acquired(expression, otherInventory));
    }

    private static EuFormexEligibilityPopulation Eligibility(params LanguageScopedExpression[] expressions)
    {
        var production = Production(expressions);
        var enumerations = expressions.Select(FormexEnumeration).ToArray();
        return EuFormexEligibilityPopulation.TryCreate(
            production, enumerations, out var refusal, out var detail)
            ?? throw new AssertFailedException($"{refusal}: {detail}");
    }

    private static EuFormexEligibilityPopulation EligibilityWithIneligible(
        LanguageScopedExpression eligible,
        LanguageScopedExpression ineligible)
    {
        var production = Production([eligible, ineligible]);
        return EuFormexEligibilityPopulation.TryCreate(
            production,
            [FormexEnumeration(eligible), Enumeration(ineligible, "html")],
            out var refusal,
            out var detail) ?? throw new AssertFailedException($"{refusal}: {detail}");
    }

    private static EuFormexManifestationEnumerationResult FormexEnumeration(
        LanguageScopedExpression expression) => Enumeration(expression, "fmx4");

    private static EuFormexManifestationEnumerationResult Enumeration(
        LanguageScopedExpression expression,
        string manifestationType)
    {
        var profile = EuFormexManifestationDiscoveryPlan.Create().CreateDeliveryProfile();
        var values = new Dictionary<string, RepeatedEnumerationRdfTerm>(StringComparer.Ordinal)
        {
            ["work"] = RepeatedEnumerationRdfTerm.Iri(expression.Identity.PublisherWorkId),
            ["expression"] = RepeatedEnumerationRdfTerm.Iri(expression.Identity.PublisherExpressionId),
            ["manifestation"] = RepeatedEnumerationRdfTerm.Iri(
                expression.Identity.PublisherExpressionId + ".01"),
            ["manifestation_type"] = RepeatedEnumerationRdfTerm.Literal(manifestationType,
                "http://www.w3.org/2001/XMLSchema#string", null),
            ["manifestation_type_kind"] = RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            ["datatype_iri"] = RepeatedEnumerationRdfTerm.Literal(
                "http://www.w3.org/2001/XMLSchema#string", null, null),
            ["language_tag"] = RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            ["multiplicity"] = RepeatedEnumerationRdfTerm.Literal("1",
                "http://www.w3.org/2001/XMLSchema#integer", null),
            ["key_1"] = RepeatedEnumerationRdfTerm.Literal(
                expression.Identity.PublisherExpressionId + ".01", null, null),
            ["key_2"] = RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            ["key_3"] = RepeatedEnumerationRdfTerm.Literal(manifestationType, null, null),
            ["key_4"] = RepeatedEnumerationRdfTerm.Literal(
                "http://www.w3.org/2001/XMLSchema#string", null, null),
            ["key_5"] = RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        };
        var terms = profile.ProjectionVariables.Select(name => values[name]).ToArray();
        return EuFormexManifestationEnumerationProducer.DecodeRows(
            [new RepeatedEnumerationRow(terms, terms, terms)], profile,
            AbsenceFixtures.Delivery(
                EuFormexManifestationDiscoveryPlan.PartitionKeyFor(expression.Identity), 1).Proof,
            expression, LuxembourgAcquisitionTestFixture.TestBudgetSnapshot());
    }

    private static LanguageScopedExpression Expression(
        string expressionIri,
        string language = "http://publications.europa.eu/resource/authority/language/ENG") =>
        LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(Work, expressionIri), language, null,
            new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar,
                new SourceRegistryMemberRef(Artifact('b'), "expression"), expressionIri,
                expressionIri[(expressionIri.LastIndexOf('/') + 1)..],
                Sha(expressionIri[(expressionIri.LastIndexOf('/') + 1)..]),
                Artifact('c'), null),
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, Receipt())]));

    private static EuLanguageScopedExpressionProductionResult Production(
        IReadOnlyList<LanguageScopedExpression> expressions)
    {
        var proof = AbsenceFixtures.Delivery("expression-facts", expressions.Count).Proof;
        var constructor = typeof(EuLanguageScopedExpressionDerivation).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(AbsenceFamilyEnumerationProof), typeof(AbsenceFamilyEnumerationProof),
                typeof(IReadOnlyList<LanguageScopedExpression>), typeof(byte[]), typeof(byte[])], null)!;
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
            CustodySchemaIds.DurableBlobWriteReceipt, reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence, reference,
                CustodyVerificationProfile.FileSystemUnenforced1, null,
                CustodyProtection.NotEnforced, DateTimeOffset.Parse("2026-09-14T00:00:00Z"), null));
    }

    private static SourceArtifactRef Artifact(char fill) => new(
        $"urn:uuid:{new string(fill, 8)}-{new string(fill, 4)}-4{new string(fill, 3)}-8{new string(fill, 3)}-{new string(fill, 12)}",
        new string(fill, 64));

    private static string Sha(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<EuFormexAnnexInventory> InventoryAsync(string suffix)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync(
            new byte[] { 1 }, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var package = FormexPackage(suffix);
        var request = HttpLogicalRequest.Create(
            "https://publications.europa.eu/resource/cellar/" + package.BodyRef.CanonicalKey,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("accept", "application/zip;mtype=fmx4")],
            new HttpLogicalRequestBody(0, Sha([])), new string('3', 64), new string('4', 64));
        var hop = RoutedHttpHop.Create(
            0, "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", null,
            Sha(request.CopyCanonicalBytes()), request.Uri, 200, Headers(1),
            "2026-09-15T00:00:00.0000000Z", "2026-09-15T00:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(1), 1,
            receipt.Reference.ContentSha256, DurableBlobWriteReceiptDigest.Of(receipt),
            1, receipt.Reference.ContentSha256);
        var response = RoutedHttpEvidence.Create(
            Artifact('5'), 1, 0, [hop], new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
        var binding = new EuFormexAnnexTransportBinding(package, request, response, receipt);
        return new EuFormexAnnexInventory(binding, Artifact('6'), []);
    }

    private static EuFormexPackage FormexPackage(string suffix)
    {
        var registry = Artifact('1');
        var profile = Artifact('2');
        var boundary = new EuWemiIdentityBoundary(registry, profile);
        var workKey = Work[(Work.LastIndexOf('/') + 1)..];
        var work = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar,
            new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(EuWemiRole.Work)),
            Work, workKey, Sha(workKey), profile, null);
        var expression = Object(boundary, registry, profile, workKey + suffix, EuWemiRole.Expression, work);
        var manifestation = Object(
            boundary, registry, profile, workKey + suffix + ".01", EuWemiRole.Manifestation, expression);
        var item = Object(
            boundary, registry, profile, workKey + suffix + ".01/FORMEX", EuWemiRole.Item, manifestation);
        var stream = EuFormexStreamName.TryParse(
            "CL2026R1965EN0000010.0001.xml", "32026R1965", out var roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var items = EuFormexItemSet.TryAdmit(
            [new EuFormexItem(boundary, stream, item, 0)], out roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var package = EuFormexPackage.TryAdmit(
            boundary, manifestation, expression, items, "EN", out var packageRefusal)!;
        Assert.AreEqual(EuFormexPackageRefusal.None, packageRefusal);
        return package;
    }

    private static SourceObjectRef Object(
        EuWemiIdentityBoundary boundary,
        SourceArtifactRef registry,
        SourceArtifactRef profile,
        string key,
        EuWemiRole role,
        SourceObjectRef parent) =>
        boundary.Require(
            new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar,
                new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
                "http://publications.europa.eu/resource/cellar/" + key,
                key, Sha(key), profile,
                new SourceObjectKeyRef(
                    parent.EntityKind, parent.PublisherUri,
                    parent.CanonicalKey, parent.CanonicalKeySha256)),
            role, nameof(parent));

    private static RoutedHttpResponseHeaders Headers(ulong length)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader("application/zip;mtype=fmx4"),
            new RoutedHttpSingleHeader(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
    }

    private static string Sha(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
