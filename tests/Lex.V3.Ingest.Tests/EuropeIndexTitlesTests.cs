using System.Reflection;
using System.Security.Cryptography;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU index /7 (Release 3's item 1): the census expressions' titles, read from family X's literals as the run delivered
/// them, the admitted main bodies' titles and Official Journal references, read from their packages' markup, each held with the
/// raw form and the source it was read from and read again from them at every open; a search by title, folded as the text is;
/// two builds of one envelope byte for byte; and a replay digest that keeps its bytes, since the titles stand outside it.
/// </summary>
[TestClass]
public sealed class EuropeIndexTitlesTests
{
    private const string EnglishExpression = "http://publications.europa.eu/resource/cellar/5f2552c2-11bd-11e6-ba9a-01aa75ed71a1.0001";
    private const string FrenchExpression = "http://publications.europa.eu/resource/cellar/5f2552c2-11bd-11e6-ba9a-01aa75ed71a1.0002";

    [TestMethod]
    public async Task FamilyXsTitleLiteralsAreObservedForTheEnglishAndFrenchExpressionsOfTheCensusOnly()
    {
        var titles = EuTitlesFixture.Gdpr(EnglishExpression, null)
            .Append(new EuAxiomWiringHarness.ExpressionTitle(FrenchExpression, EuAcquisitionTestFixture.ExpressionTitle, "Datenschutz-Grundverordnung"))
            .ToArray();
        var run = await EuAxiomWiringHarness.RunAsync(static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root),
            seedCelex: "32016R0679", expressionIri: EnglishExpression, additionalExpressionIri: FrenchExpression,
            additionalExpressionLanguageAuthority: "http://publications.europa.eu/resource/authority/language/DEU", expressionTitles: titles);
        Assert.IsNull(run.Refusal, run.Refusal?.Detail);
        var observed = run.ObservedExpressionTitles;
        Assert.HasCount(2, observed, "a German expression's title is not read: Lex acquires English and French expressions only");
        var root = EuAxiomWiringHarness.SeedRoot("32016R0679");
        Assert.IsTrue(observed.All(value => value.PublisherWorkIri == root && value.PublisherExpressionIri == EnglishExpression));
        Assert.IsTrue(observed.All(static value => value.LanguageIri == "http://publications.europa.eu/resource/authority/language/ENG"));
        var title = observed.Single(static value => value.PredicateIri.EndsWith("#expression_title", StringComparison.Ordinal));
        Assert.AreEqual(EuTitlesFixture.EnglishTitle, title.RawLexical);
        Assert.IsNull(title.RawLanguageTag);
        var shortTitle = observed.Single(static value => value.PredicateIri.EndsWith("#expression_title_short", StringComparison.Ordinal));
        Assert.AreEqual(EuTitlesFixture.EnglishShortTitle, shortTitle.RawLexical);
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", shortTitle.RawDatatypeIri, "the typed literal keeps its datatype");
        Assert.AreEqual(1, observed.Select(static value => value.InterpretationProfileRef).Distinct().Count(), "each names the family-X batch it was read from");
    }

    [TestMethod]
    public async Task TheReplayDigestsKeepTheirBytesBecauseTheTitlesStandOutsideThem()
    {
        // The query checkpoint's result digest (schema 3 and its prior schema 2) and the Formex population's input digest are
        // computed from the run with its titles and from the very same run with none: they are the same bytes, so a checkpoint
        // written before the titles were read (run 16's query checkpoint 37a649de…, which EuTitlesRun16Probe reopens against
        // its stored digest) reopens unchanged.
        const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        var run = await EuAxiomWiringHarness.RunAsync(static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root),
            seedCelex: "32016R0679", expressionIri: EnglishExpression, expressionTitles: EuTitlesFixture.Gdpr(EnglishExpression, null));
        Assert.HasCount(2, run.ObservedExpressionTitles);
        var untitled = (EuQueryExecutionResult)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(run, null)!;
        typeof(EuQueryExecutionResult).GetField("<ObservedExpressionTitles>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(untitled, Array.Empty<EuObservedExpressionTitle>());
        Assert.IsEmpty(untitled.ObservedExpressionTitles);

        var resultDigest = typeof(EuQueryExecutionAdapter).GetMethod("RunResultDigest", Static)!;
        foreach (var prior in new[] { false, true })
            Assert.AreEqual((string)resultDigest.Invoke(null, [untitled, prior])!, (string)resultDigest.Invoke(null, [run, prior])!,
                $"the query checkpoint's result digest (prior schema: {prior}) names no title");
        var inputDigest = typeof(EuFormexPackagePopulationProducer).GetMethod("PopulationInputDigest", Static)!;
        foreach (var prior in new[] { false, true })
            Assert.AreEqual((string)inputDigest.Invoke(null, [untitled, prior])!, (string)inputDigest.Invoke(null, [run, prior])!,
                $"the Formex population's input digest (prior schema: {prior}) names no title");
    }

    [TestMethod]
    public async Task TheIndexHoldsEachTitleAndReferenceWithItsRawFormAndSourceAndBuildsTheSameBytesTwice()
    {
        var envelope = await EuropeIndexBuilderTests.RetainedGdprEnvelopeAsync(withTitles: true);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var again = EuropeIndexBuilder.TryBuild(envelope, out _, out _)!;
        CollectionAssert.AreEqual(built.IndexBytes.ToArray(), again.IndexBytes.ToArray(), "the titles and references build deterministically");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.IsTrue(reader.HasTitles);

        var titles = reader.ReadTitles([EnglishExpression]);
        CollectionAssert.AreEqual(
            new[] { EuropeTitleSources.CellarExpressionTitle, EuropeTitleSources.CellarExpressionTitleShort, EuropeTitleSources.FormexTitle },
            titles.Select(static row => row.Source).ToArray(), "the publisher's title, its short title, and the package's title, in source order");
        Assert.IsTrue(titles.All(static row => row.Language == "eng"));
        Assert.AreEqual(EuTitlesFixture.EnglishTitle, titles[0].TitleText);
        Assert.AreEqual(EuTitlesFixture.EnglishShortTitle, titles[1].TitleText);
        Assert.AreEqual(EuropeTitleKinds.ShortTitle, titles[1].TitleKind);
        Assert.AreEqual(EuTitlesFixture.EnglishTitle, titles[2].TitleText, "the package's title, its markup removed, is the publisher's");
        StringAssert.StartsWith(titles[2].RawValue, "<TITLE><TI><P><HT TYPE=\"UC\">Regulation</HT>", "the markup is the raw form kept");
        var package = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(static outcome => outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted).Source.AcquiredInventory!;
        Assert.AreEqual(package.SourceReceipt.Reference.ContentSha256, titles[2].SourceSha256, "the package it was read from");
        var observed = envelope.BodyComposition.Envelope.Europe.ObservedExpressionTitles;
        Assert.AreEqual(observed[0].InterpretationProfileRef.Sha256, titles[0].SourceSha256, "the family-X batch it was read from");

        var reference = reader.ReadOfficialJournalReferences([EnglishExpression]).Single();
        Assert.AreEqual((EuOfficialJournalReferenceKinds.ActPublication, EuOfficialJournalNumberForms.SeriesNumber, "L", "119", "2016", "1", "2016-05-04"),
            (reference.Kind, reference.NumberForm, reference.Series, reference.OjNumber, reference.Year, reference.FirstPage, reference.PublicationDate));
        Assert.AreEqual(package.SourceReceipt.Reference.ContentSha256, reference.PackageSha256);
        Assert.IsEmpty(reader.ReadOfficialJournalReferences(["http://publications.europa.eu/resource/cellar/not-held.0001"]));

        // A search by title, folded as the text is: a phrase of a title, the whole title, a short title, capitals and accents.
        Assert.IsTrue(reader.SearchTitles("general data protection regulation").Any(static match => match.Title.Source == EuropeTitleSources.CellarExpressionTitle && !match.WholeTitle));
        Assert.IsTrue(reader.SearchTitles(EuTitlesFixture.EnglishTitle.ToUpperInvariant()).Any(static match => match.WholeTitle));
        Assert.IsTrue(reader.SearchTitles("GDPR").Any(static match => match.Title.TitleKind == EuropeTitleKinds.ShortTitle));
        Assert.IsEmpty(reader.SearchTitles("a phrase no title holds"));
        Assert.IsEmpty(reader.SearchTitles("   "), "a query that folds to white space matches nothing");
    }

    [TestMethod]
    public async Task AFrenchExpressionWhosePackageStatesNeitherHoldsOnlyThePublishersTitles()
    {
        var envelope = await EuropeIndexBuilderTests.RetainedGdprEnvelopeAsync(acquireFrenchExpression: true, withTitles: true);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        using var reader = EuropeIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        CollectionAssert.AreEqual(new[] { EuropeTitleSources.CellarExpressionTitle, EuropeTitleSources.CellarExpressionTitleShort },
            reader.ReadTitles([FrenchExpression]).Select(static row => row.Source).ToArray(), "the synthetic package states no TITLE");
        Assert.IsEmpty(reader.ReadOfficialJournalReferences([FrenchExpression]), "nor any DOCUMENT.REF");
        Assert.AreEqual("fra", reader.ReadTitles([FrenchExpression])[0].Language);
        Assert.IsTrue(reader.SearchTitles("REGLEMENT GENERAL SUR LA PROTECTION DES DONNEES").Any(static match => match.Title.Language == "fra"),
            "capitals and accents ignored");
    }

    [TestMethod]
    [DataRow("UPDATE expression_titles SET title_text='Regulation (EU) 2016/680' WHERE source='cellar_expression_title'")]
    [DataRow("UPDATE expression_titles SET raw_value=replace(raw_value,'Regulation','Directive') WHERE source='formex_title'")]
    [DataRow("UPDATE expression_titles SET title_kind='short_title' WHERE source='cellar_expression_title'")]
    [DataRow("UPDATE expression_titles SET language='fra'")]
    [DataRow("DELETE FROM expression_titles WHERE source='cellar_expression_title_short'")]
    [DataRow("UPDATE official_journal_references SET oj_number='120'")]
    [DataRow("UPDATE official_journal_references SET publication_date='2016-05-05'")]
    [DataRow("UPDATE official_journal_references SET kind='consolidated_act_publication'")]
    [DataRow("DELETE FROM official_journal_references")]
    [DataRow("DROP TABLE expression_titles")]
    [DataRow("PRAGMA user_version=6")]
    [DataRow("UPDATE stamp SET schema_identity='lex-v3-europe-index/6'")]
    public async Task TheReaderRefusesAChangedMissingOrRelabelledTitleOrReference(string sql)
    {
        var envelope = await EuropeIndexBuilderTests.RetainedGdprEnvelopeAsync(withTitles: true);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        var bytes = Mutate(built.IndexBytes.ToArray(), sql, restamp: false);
        Assert.ThrowsExactly<InvalidDataException>(() => Open(bytes, built, corpus.ArtifactRef));
    }

    [TestMethod]
    [DataRow("UPDATE expression_titles SET title_text='Regulation (EU) 2016/680' WHERE source='cellar_expression_title'", "title")]
    [DataRow("UPDATE expression_titles SET raw_value='<TITLE><TI><P>Another act</P></TI></TITLE>', title_text='Another act' WHERE source='formex_title'", "title")]
    [DataRow("UPDATE expression_titles SET publisher_work_iri='http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000' WHERE source='cellar_expression_title'", "title")]
    [DataRow("UPDATE official_journal_references SET oj_number='120'", "Official Journal")]
    [DataRow("UPDATE official_journal_references SET publication_date=NULL", "Official Journal")]
    public async Task LogicalRestampingCannotHideATitleOrReferenceItsRawFormDoesNotSay(string sql, string expected)
    {
        var envelope = await EuropeIndexBuilderTests.RetainedGdprEnvelopeAsync(withTitles: true);
        var built = EuropeIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var corpus = LexCorpus6Builder.TryBuild(envelope, out _, out _)!;
        var bytes = Mutate(built.IndexBytes.ToArray(), sql, restamp: true);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => Open(bytes, built, corpus.ArtifactRef));
        StringAssert.Contains(exception.Message, expected);
    }

    private static EuropeIndexReader Open(byte[] bytes, EuropeIndexBuildResult built, SourceArtifactRef corpus)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(PublisherId.EuEurLex, sha,
            built.CapabilityManifest.Cells.Select(cell => new V3IndexCapabilityCell(cell.Publisher, sha, cell.Operation, cell.Column, cell.Field,
                cell.Language, cell.PeriodFrom, cell.PeriodTo, cell.Population)).ToArray(), out var manifest, out var refusal), refusal.ToString());
        return EuropeIndexReader.OpenAndVerify(new(LexCorpus6Builder.ResourceIdOf(sha), sha), bytes, corpus, manifest!);
    }

    /// <summary>The index with <paramref name="sql"/> applied and, when asked, its logical rows re-sealed as the reader reads them.</summary>
    private static byte[] Mutate(byte[] source, string sql, bool restamp)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-eu-titles-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, source);
            using (var connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadWrite))
            {
                EuropeIndexBuilder.Execute(connection, sql);
                if (restamp)
                {
                    var logical = EuropeIndexBuilder.HashLogicalRows(
                        Rows<EuropeIndexBuilder.MemberRow>(connection, "ReadMembers"),
                        Rows<EuropeIndexBuilder.CorrigendumLineRow>(connection, "ReadLines"),
                        Rows<EuropeIndexBuilder.CorrigendumGapRow>(connection, "ReadGaps"),
                        Rows<EuropeIndexBuilder.ArticleRow>(connection, "StreamArticles"),
                        Rows<EuropeIndexBuilder.ArticleSourceRow>(connection, "StreamArticleSources"),
                        Rows<EuropeIndexBuilder.ArticleDigestRow>(connection, "StreamArticleDigests"),
                        Rows<EuropeIndexState>(connection, "ReadStates"),
                        EuropeIndexReader.ReadForceDateRows(connection),
                        EuropeIndexReader.ReadTitleRowsOf(connection),
                        EuropeIndexReader.ReadReferenceRowsOf(connection));
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE stamp SET logical_rows_sha256=$logical";
                    command.Parameters.AddWithValue("$logical", logical);
                    command.ExecuteNonQuery();
                }
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            EuropeIndexBuilder.DeleteDatabase(path);
        }
    }

    private static T[] Rows<T>(SqliteConnection connection, string method) =>
        ((IEnumerable<T>)typeof(EuropeIndexReader).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [connection])!).ToArray();
}
