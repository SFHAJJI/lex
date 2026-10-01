using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexMainBodyLegalContentProducerTests
{
    [TestMethod]
    [DataRow("eng", "EN")]
    [DataRow("fra", "FR")]
    public async Task RetainedConsolidatedPackagesProduceTheirOwnOrderedArticles(string fixtureLanguage, string language)
    {
        var bytes = await ReadConsolidatedPackage(fixtureLanguage);
        Assert.AreEqual(fixtureLanguage == "eng"
            ? "cd8d38e54cc111b17ea2dcae8f1e0e265e3255cc8b76776e8160706b39d13491"
            : "37e5d9e73433d7586d787331974e11ba7e837212b32cba3d1b0bb0d26b5cf859", Sha(bytes));
        var outcome = await Produce(bytes, language);
        Assert.HasCount(99, outcome.Articles);
        AssertDigests(bytes, outcome.Articles);
        CollectionAssert.AreEqual(Enumerable.Range(1, 99).Select(n => n.ToString("D3")).ToArray(),
            outcome.Articles.Select(a => a.PublisherIdentifier).ToArray());
        Assert.IsTrue(outcome.Articles.All(a => a.Language == language && a.PublisherDate == "20160504"),
            "The document bibliographic date remains distinct from CONSLEG.DATE=20180523.");
        Assert.IsTrue(outcome.Articles.All(a => a.PackageEntry.StartsWith("CL2016R0679", StringComparison.Ordinal)));
        Assert.AreEqual("Article 1", outcome.Articles[0].Heading);
    }

    [TestMethod]
    [DataRow("duplicate-document")]
    [DataRow("duplicate-bibliography")]
    [DataRow("duplicate-operative-text")]
    [DataRow("duplicate-language")]
    [DataRow("duplicate-date")]
    [DataRow("missing-date")]
    [DataRow("wrong-language")]
    [DataRow("missing-operative-text")]
    public async Task ConsolidatedDocumentAmbiguityOrMissingCoordinatesRefuses(string change)
    {
        var bytes = RewriteConsolidated(await ReadConsolidatedPackage("eng"), root =>
        {
            var doc = root.Element("CONS.DOC")!;
            var bib = doc.Element("BIB.INSTANCE")!;
            switch (change)
            {
                case "duplicate-document": root.Add(new XElement(doc)); break;
                case "duplicate-bibliography": doc.Add(new XElement(bib)); break;
                case "duplicate-operative-text": doc.Add(new XElement(doc.Element("ENACTING.TERMS")!)); break;
                case "duplicate-language": bib.Add(new XElement(bib.Element("LG.DOC")!)); break;
                case "duplicate-date": bib.Add(new XElement(bib.Element("DATE")!)); break;
                case "missing-date": bib.Element("DATE")!.Remove(); break;
                case "wrong-language": bib.Element("LG.DOC")!.Value = "FR"; break;
                case "missing-operative-text": doc.Element("ENACTING.TERMS")!.Remove(); break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        });
        var result = await Produce(bytes, requireAdmitted: false);
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape, result.Disposition);
        Assert.HasCount(0, result.Articles);
    }

    [TestMethod]
    public async Task ConsolidatedBibliographicHistoryAndNonOperativeArticlesCannotEnterLegalText()
    {
        var bytes = RewriteConsolidated(await ReadConsolidatedPackage("eng"), root =>
        {
            var doc = root.Element("CONS.DOC")!;
            doc.Element("FAM.COMP")!.Add(new XElement("LG.DOC", "FR"));
            var annex = new XElement("ANNEX");
            var note = new XElement("NOTE");
            doc.Element("ENACTING.TERMS")!.Add(annex, note);
            foreach (var parent in new[] { root, doc.Element("PREAMBLE")!, doc.Element("FINAL")!, annex, note })
                parent.Add(new XElement("ARTICLE", new XAttribute("IDENTIFIER", "999"),
                    new XElement("TI.ART", "History only"), new XElement("P", "EXCLUDED_SENTINEL")));
        });
        var result = await Produce(bytes);
        Assert.HasCount(99, result.Articles);
        Assert.IsFalse(result.Articles.Any(a => a.SearchableText.Contains("EXCLUDED_SENTINEL", StringComparison.Ordinal)));
    }

    private static Task<byte[]> ReadConsolidatedPackage(string language) => File.ReadAllBytesAsync(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", $"gdpr-consolidated-{language}-fmx4-body.bin"));

    private static byte[] RewriteConsolidated(byte[] package, Action<XElement> rewrite)
    {
        using var input = new MemoryStream(package);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var main = archive.Entries.Single(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)
            && !e.FullName.EndsWith(".doc.xml", StringComparison.Ordinal));
        return RewriteEntry(package, main.FullName, bytes =>
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.PreserveWhitespace);
            rewrite(document.Root!);
            return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        });
    }

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

    private static async Task<EuFormexMainBodyLegalContentOutcome> Produce(byte[] bytes, string language = "EN", bool requireAdmitted = true)
    {
        var fixture = await EuFormexAnnexInventoryProducerTests.FixtureAsync(bytes);
        var inventory = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference, CancellationToken.None);
        Assert.IsNotNull(inventory.Inventory, inventory.Detail);
        var expression = LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(fixture.Binding.Expression.ParentKeyRef!.PublisherUri,
                fixture.Binding.Expression.PublisherUri), language, null, fixture.Binding.Expression,
            LanguageScopedExpressionLineage.FromContributions(
                [new(LanguageScopedExpressionContribution.IdentityAndLanguage, fixture.Receipt)]));
        var run = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(run,
            [EuFormexPackageOutcome.Acquired(expression, inventory.Inventory)]);
        var result = await new EuFormexMainBodyLegalContentProducer(fixture.Store).RunAsync(formex, CancellationToken.None);
        var outcome = result.Outcomes.Single();
        if (requireAdmitted)
            Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, outcome.Disposition, outcome.Detail);
        return outcome;
    }

}
