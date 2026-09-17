using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgIndexBuilderTests
{
    private const string Retained1991 = "loi-1991-08-10-n3--2024-02-01--fr.bin";

    [TestMethod]
    public void BuilderAndStrictReaderShipAsOneTerminalSlice()
    {
        var schema = (string)typeof(LuxembourgIndexBuilder)
            .GetField(nameof(LuxembourgIndexBuilder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-v3-luxembourg-index/1", schema);
        Assert.IsNotNull(typeof(LuxembourgIndexBuilder).GetMethod(nameof(LuxembourgIndexBuilder.TryBuild)));
        Assert.IsNotNull(typeof(LuxembourgIndexReader).GetMethod(nameof(LuxembourgIndexReader.OpenAndVerify)));
    }

    [TestMethod]
    public async Task CompleteEnvelopeBuildsDeterministicLuOnlyIndexAndMeasuredManifest()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");

        var first = LuxembourgIndexBuilder.TryBuild(envelope, out var firstRefusal, out var firstDetail);
        var second = LuxembourgIndexBuilder.TryBuild(envelope, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.IndexBytes.ToArray(), second.IndexBytes.ToArray());
        Assert.AreEqual(first.IndexRef, second.IndexRef);
        Assert.AreEqual(first.CapabilityManifestRef, second.CapabilityManifestRef);
        CollectionAssert.AreEqual(
            first.CapabilityManifestBytes.ToArray(),
            second.CapabilityManifestBytes.ToArray());
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            second.CapabilityManifest.Cells.ToArray());
        Assert.AreEqual(PublisherId.LuLegilux, first.CapabilityManifest.Publisher);
        Assert.AreEqual(first.IndexRef.Sha256, first.CapabilityManifest.IndexSha256);
        var reopenedManifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            first.CapabilityManifestRef,
            first.CapabilityManifestBytes.Span,
            PublisherId.LuLegilux,
            first.IndexRef.Sha256);
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            reopenedManifest.Cells.ToArray());

        using var reader = LuxembourgIndexReader.OpenAndVerify(
            first.IndexRef,
            first.IndexBytes.Span,
            corpus.ArtifactRef,
            first.CapabilityManifest);
        var expectedLuMembers = corpus.VerifiedSet.Set.Members.Count(static member =>
            member.Publisher == PublisherId.LuLegilux);
        Assert.AreEqual(expectedLuMembers, reader.MemberCount);
        Assert.IsTrue(reader.MemberCount < corpus.VerifiedSet.Set.Members.Count,
            "The Luxembourg index must not contain the EU population.");
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static member =>
            member.Publisher == PublisherId.LuLegilux &&
            member.Outcome == LexCorpus6OutcomeKind.RightsWithheld),
            "The accepted fixture must exercise the retained rights-withheld terminal path.");
        Assert.AreEqual(0, reader.ArticleCount,
            "A rights-withheld member remains population evidence but cannot become legal text.");
        Assert.IsEmpty(first.CapabilityManifest.Cells,
            "No searchable row means no advertised capability.");
        var unsupported = reader.Search(
            "deu", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1), "law");
        Assert.AreEqual(
            V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex,
            unsupported.Outcome);
        Assert.IsEmpty(unsupported.ArticleIdentities);
    }

    [TestMethod]
    public async Task RetainedPublisherAknProducesDayExactCapabilitiesWithoutAdvertisingTheGap()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
        var xml = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", Retained1991));
        Assert.AreEqual(
            "3a6bb598a9310f8a31240c1f33ae357d6e1f7a46392ca33d223c2718cdded95c",
            Convert.ToHexStringLower(SHA256.HashData(xml)));
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg,
            luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        var luxembourgMembers = corpus.VerifiedSet.Set.Members.Where(static value =>
            value.Publisher == PublisherId.LuLegilux).ToArray();
        var member = luxembourgMembers.Single(static value =>
            value.Outcome == LexCorpus6OutcomeKind.Acquired);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, member.Outcome);
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(luxembourgMembers.Length, reader.MemberCount);
        Assert.AreEqual(49, reader.ArticleCount);
        Assert.HasCount(5, built.CapabilityManifest.Cells);
        var cells = built.CapabilityManifest.Cells.OrderBy(static cell => cell.PeriodFrom).ToArray();
        Assert.Fail(string.Join(";", cells.Select(static cell => $"{cell.PeriodFrom:yyyy-MM-dd}:{cell.Population}")));
        Assert.AreEqual(new DateOnly(2021, 8, 22), cells[0].PeriodFrom);
        Assert.AreEqual(cells[0].PeriodFrom, cells[0].PeriodTo);
        Assert.AreEqual(39, cells[0].Population);
        Assert.AreEqual(new DateOnly(2024, 2, 1), cells[^1].PeriodFrom);
        Assert.AreEqual(cells[^1].PeriodFrom, cells[^1].PeriodTo);
        Assert.AreEqual(1, cells[^1].Population);
        var gap = reader.Search(
            "fra", new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), "article");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, gap.Outcome);
        Assert.IsEmpty(gap.ArticleIdentities);
    }

    [TestMethod]
    public async Task AdmittedAknTextWithoutAdmittingRightsNeverBecomesSearchable()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta><body>
                <article id="art_1"><scl:JOLUXWork>
                  <scl:jolux scl:name="dateApplicability">2026-02-03</scl:jolux>
                </scl:JOLUXWork><content><p>Withheld publisher words.</p></content></article>
              </body></act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(
                xml, store, manifestation, item, includeEndpointLicence: false);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        Assert.IsTrue(envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
            .Any(static value => value.Disposition == LuxembourgAknLegalContentDisposition.Admitted));
        Assert.IsTrue(corpus.VerifiedSet.Set.Members.Any(static value =>
            value.Publisher == PublisherId.LuLegilux &&
            value.Outcome == LexCorpus6OutcomeKind.RightsWithheld));

        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNotNull(built, $"{refusal}: {detail}");
        using var reader = LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, built.CapabilityManifest);
        Assert.AreEqual(0, reader.ArticleCount);
        Assert.IsEmpty(built.CapabilityManifest.Cells);
    }

    [TestMethod]
    public async Task CorpusIncompletenessRefusesBeforeAnIndexExists()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(includeLegalNotice: false);

        var result = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNull(result);
        Assert.AreEqual(LuxembourgIndexBuildRefusal.CorpusRefused, refusal, detail);
    }

    [TestMethod]
    public async Task StrictReaderRejectsByteCorpusAndCapabilitySubstitution()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var changedBytes = built.IndexBytes.ToArray();
        changedBytes[^1] ^= 1;
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, changedBytes, corpus.ArtifactRef, built.CapabilityManifest));

        var otherCorpus = new SourceArtifactRef(corpus.ArtifactRef.ResourceId, new string('a', 64));
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, otherCorpus, built.CapabilityManifest));

        var foreignCorpusIdentity = new SourceArtifactRef(
            "urn:uuid:ffffffff-ffff-5fff-8fff-ffffffffffff", corpus.ArtifactRef.Sha256);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, foreignCorpusIdentity, built.CapabilityManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            new string('b', 64),
            [],
            out var wrongManifest,
            out _));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, wrongManifest!));

        var extendedBytes = AddUnexpectedTable(built.IndexBytes.Span);
        var extendedDigest = Convert.ToHexStringLower(SHA256.HashData(extendedBytes));
        var extendedRef = new SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(extendedDigest), extendedDigest);
        var extendedManifest = RebindManifest(built.CapabilityManifest, extendedDigest);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            extendedRef, extendedBytes, corpus.ArtifactRef, extendedManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            built.IndexRef.Sha256,
            [new V3IndexCapabilityCell(
                PublisherId.LuLegilux, built.IndexRef.Sha256, "search", "articles",
                "searchable_text", "fra", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 1), 1)],
            out var inventedManifest,
            out var inventedRefusal), inventedRefusal.ToString());
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, inventedManifest!));

        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE members SET outcome='invented' WHERE rowid=(SELECT min(rowid) FROM members)");
        AssertTamperedDatabaseRejected(
            built, corpus.ArtifactRef,
            "UPDATE stamp SET sqlite_version='0.0.0' WHERE stamp_id=1");
    }

    private static V3IndexCapabilityManifest RebindManifest(
        V3IndexCapabilityManifest source,
        string indexSha256)
    {
        var cells = source.Cells.Select(cell => new V3IndexCapabilityCell(
            cell.Publisher, indexSha256, cell.Operation, cell.Column, cell.Field,
            cell.Language, cell.PeriodFrom, cell.PeriodTo, cell.Population));
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            source.Publisher, indexSha256, cells, out var rebound, out var refusal),
            refusal.ToString());
        return rebound!;
    }

    private static byte[] AddUnexpectedTable(ReadOnlySpan<byte> source)
        => MutateDatabase(source, "CREATE TABLE invented(value TEXT) STRICT");

    private static void AssertTamperedDatabaseRejected(
        LuxembourgIndexBuildResult built,
        SourceArtifactRef corpusRef,
        string sql)
    {
        var bytes = MutateDatabase(built.IndexBytes.Span, sql);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var reference = new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest);
        var manifest = RebindManifest(built.CapabilityManifest, digest);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, bytes, corpusRef, manifest));
    }

    private static byte[] MutateDatabase(ReadOnlySpan<byte> source, string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-index-hostile-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, source.ToArray());
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            connection.Close();
            return File.ReadAllBytes(path);
        }
        finally
        {
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }
}
