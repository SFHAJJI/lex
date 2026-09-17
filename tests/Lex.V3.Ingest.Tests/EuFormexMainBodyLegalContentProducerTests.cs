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
        var article4 = outcome.Articles.Single(static article => article.PublisherIdentifier == "004");
        var firstDefinition = article4.Tokens.Select(static token => token.Text).ToArray();
        var number = Array.IndexOf(firstDefinition, "(1)");
        Assert.IsGreaterThanOrEqualTo(0, number);
        CollectionAssert.AreEqual(
            new[] { "(1)", "‘", "personal data", "’" },
            firstDefinition.Skip(number).Take(4).ToArray(),
            "List numbering and publisher quotation markers must remain separate ordered tokens.");
        Assert.IsTrue(article4.Tokens.Any(static token =>
            token.Kind == EuFormexMainBodyTokenKind.Footnote),
            "Inline notes must remain separate ordered note tokens.");
    }

    [TestMethod]
    public async Task MixedRetainedPackageSelectsOnlyTheActMainBodyByRootRole()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "new-fmx4-200-body.bin"));
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes);
        var inventoryResult = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventoryResult.Inventory, inventoryResult.Detail);
        Assert.HasCount(1, inventoryResult.Inventory.Members,
            "The retained package also contains one ANNEX unit, which is not main-body text.");
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(
                fixture.Binding.Expression.ParentKeyRef!.PublisherUri,
                fixture.Binding.Expression.PublisherUri),
            "EN", null, fixture.Binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, fixture.Receipt)]));
        var acquired = EuFormexPackageOutcome.Acquired(expression, inventoryResult.Inventory);
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(run, [acquired]);

        var population = await new EuFormexMainBodyLegalContentProducer(fixture.Store)
            .RunAsync(formex, CancellationToken.None);

        var outcome = population.Outcomes.Single();
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, outcome.Disposition);
        Assert.HasCount(2, outcome.Articles);
        Assert.IsTrue(outcome.Articles.All(static article =>
            article.PackageEntry == "L_202601965EN.000101.fmx.xml"));
        Assert.IsFalse(outcome.Articles.Any(static article =>
            article.PackageEntry.Contains("000201", StringComparison.Ordinal)),
            "The ANNEX root must not enter the ACT article population.");
    }
}
