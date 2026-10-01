using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
        AssertDigests(bytes, outcome.Articles);
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
        var noteCitations = outcome.Articles.SelectMany(static article => article.Tokens)
            .Where(static token => token.Kind == EuFormexMainBodyTokenKind.Footnote)
            .SelectMany(static token => token.NoteBody ?? [])
            .Where(static token => token.Kind == EuFormexMainBodyTokenKind.Reference)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "OJ L 241, 17.9.2015, p.\u00a01",
                "OJ L 218, 13.8.2008, p.\u00a030",
                "OJ L 145, 31.5.2001, p. 43",
            },
            noteCitations.Select(static token => token.Text).ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "{\"COLL\":\"L\",\"DATE.PUB\":\"20150917\",\"NO.OJ\":\"241\",\"PAGE.FIRST\":\"1\"}",
                "{\"COLL\":\"L\",\"DATE.PUB\":\"20080813\",\"NO.OJ\":\"218\",\"PAGE.FIRST\":\"30\"}",
                "{\"COLL\":\"L\",\"DATE.PUB\":\"20010531\",\"NO.OJ\":\"145\",\"PAGE.FIRST\":\"43\"}",
            },
            noteCitations.Select(static token => token.Target).ToArray());
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
        AssertDigests(bytes, outcome.Articles);
        Assert.IsTrue(outcome.Articles.All(static article =>
            article.PackageEntry == "L_202601965EN.000101.fmx.xml"));
        Assert.IsFalse(outcome.Articles.Any(static article =>
            article.PackageEntry.Contains("000201", StringComparison.Ordinal)),
            "The ANNEX root must not enter the ACT article population.");
    }
    [TestMethod]
    [DataRow("comment")]
    [DataRow("bom")]
    [DataRow("line-endings")]
    public async Task SourceBytesChangeWithoutChangingEmittedTextOrSemanticIdentity(string change)
    {
        var original = await ReadMixedPackage();
        var changed = RewriteEntry(original, "L_202601965EN.000101.fmx.xml", bytes => change switch
        {
            "comment" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
                .Replace("<ACT ", "<!-- retained source comment -->\n<ACT ", StringComparison.Ordinal)),
            "bom" => new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray(),
            "line-endings" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        });
        var before = await Produce(original);
        var after = await Produce(changed);
        AssertDigests(changed, after.Articles);
        CollectionAssert.AreEqual(before.Articles.Select(a => a.IdentitySha256).ToArray(),
            after.Articles.Select(a => a.IdentitySha256).ToArray());
        CollectionAssert.AreEqual(before.Articles.Select(a => a.TextSha256).ToArray(),
            after.Articles.Select(a => a.TextSha256).ToArray());
        Assert.AreNotEqual(before.Articles[0].SourceEntrySha256, after.Articles[0].SourceEntrySha256);
    }

    [TestMethod]
    public async Task ChangingAnAnnexChangesThePackageButNotMainBodyDigests()
    {
        var original = await ReadMixedPackage();
        var changed = RewriteEntry(original, "L_202601965EN.000201.fmx.xml", bytes =>
            bytes.Concat(Encoding.UTF8.GetBytes("<!-- archive-only change -->")).ToArray());
        var before = await Produce(original);
        var after = await Produce(changed);
        Assert.AreNotEqual(Sha(original), Sha(changed));
        AssertDigests(changed, after.Articles);
        CollectionAssert.AreEqual(before.Articles.Select(a => a.SourceEntrySha256).ToArray(),
            after.Articles.Select(a => a.SourceEntrySha256).ToArray());
        CollectionAssert.AreEqual(before.Articles.Select(a => a.TextSha256).ToArray(),
            after.Articles.Select(a => a.TextSha256).ToArray());
    }

    [TestMethod]
    public async Task ChangingOneArticlesTextChangesItsTextDigestAndTheSharedSourceEntryDigest()
    {
        var original = await ReadMixedPackage();
        var changed = RewriteEntry(original, "L_202601965EN.000101.fmx.xml", bytes =>
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(
                "This Regulation shall enter into force", "This Regulation shall enter into effect", StringComparison.Ordinal)));
        var before = await Produce(original);
        var after = await Produce(changed);
        AssertDigests(changed, after.Articles);
        Assert.AreEqual(before.Articles[0].TextSha256, after.Articles[0].TextSha256);
        Assert.AreNotEqual(before.Articles[1].TextSha256, after.Articles[1].TextSha256);
        Assert.AreNotEqual(before.Articles[0].SourceEntrySha256, after.Articles[0].SourceEntrySha256);
        Assert.AreEqual(after.Articles[0].SourceEntrySha256, after.Articles[1].SourceEntrySha256);
    }

    private static Task<byte[]> ReadMixedPackage() => File.ReadAllBytesAsync(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "new-fmx4-200-body.bin"));

    private static void AssertDigests(byte[] package, IReadOnlyList<EuFormexMainBodyArticle> articles)
    {
        using var input = new MemoryStream(package);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var article in articles)
        {
            using var source = zip.GetEntry(article.PackageEntry)!.Open();
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            Assert.AreEqual(Sha(bytes.ToArray()), article.SourceEntrySha256);
            Assert.AreEqual(Sha(Encoding.UTF8.GetBytes(article.SearchableText)), article.TextSha256);
            Assert.AreNotEqual(Sha(package), article.SourceEntrySha256);
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] RewriteEntry(byte[] package, string target, Func<byte[], byte[]> rewrite)
    {
        using var input = new MemoryStream(package);
        using var original = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var changed = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in original.Entries)
            {
                using var source = entry.Open();
                using var bytes = new MemoryStream();
                source.CopyTo(bytes);
                var next = changed.CreateEntry(entry.FullName);
                next.LastWriteTime = entry.LastWriteTime;
                using var destination = next.Open();
                destination.Write(entry.FullName == target ? rewrite(bytes.ToArray()) : bytes.ToArray());
            }
        }
        return output.ToArray();
    }

    private static async Task<EuFormexMainBodyLegalContentOutcome> Produce(byte[] bytes)
    {
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes);
        var inventory = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventory.Inventory, inventory.Detail);
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(fixture.Binding.Expression.ParentKeyRef!.PublisherUri,
                fixture.Binding.Expression.PublisherUri), "EN", null, fixture.Binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, fixture.Receipt)]));
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(run,
            [EuFormexPackageOutcome.Acquired(expression, inventory.Inventory)]);
        var result = await new EuFormexMainBodyLegalContentProducer(fixture.Store).RunAsync(formex, CancellationToken.None);
        var outcome = result.Outcomes.Single();
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        return outcome;
    }

}
