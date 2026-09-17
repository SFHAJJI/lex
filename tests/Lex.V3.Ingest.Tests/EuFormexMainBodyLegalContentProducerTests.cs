using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexMainBodyLegalContentProducerTests
{
    [TestMethod]
    public async Task RetainedPublisherPackageProducesOrderedMainBodyArticles()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes);
        var inventoryResult = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventoryResult.Inventory, inventoryResult.Detail);
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(
                fixture.Binding.Expression.ParentKeyRef!.PublisherUri,
                fixture.Binding.Expression.PublisherUri),
            "EN",
            null,
            fixture.Binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, fixture.Receipt)]));
        var acquired = EuFormexPackageOutcome.Acquired(expression, inventoryResult.Inventory);
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            run, [acquired]);

        var population = await new EuFormexMainBodyLegalContentProducer(fixture.Store)
            .RunAsync(formex, CancellationToken.None);

        Assert.AreSame(formex, population.Formex);
        var outcome = population.Outcomes.Single();
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, outcome.Disposition);
        Assert.HasCount(99, outcome.Articles);
        Assert.AreEqual("001", outcome.Articles[0].PublisherIdentifier);
        Assert.AreEqual("Article 1", outcome.Articles[0].Heading);
        Assert.IsTrue(outcome.Articles[0].Tokens.Any(static token =>
            token.Text == "This Regulation lays down rules relating to the protection of natural persons with regard to the processing of personal data and rules relating to the free movement of personal data."));
    }
}
