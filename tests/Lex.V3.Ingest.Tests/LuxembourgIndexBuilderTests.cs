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
    public async Task AdmittedAknArticleProducesPublisherBoundTextDateAndMeasuredCapability()
    {
        const string manifestation =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu">
              <act>
                <meta><identification>
                  <FRBRManifestation><FRBRthis value="{{manifestation}}"/></FRBRManifestation>
                  <scl:JOLUXManifestation>
                    <scl:jolux scl:name="uriThis">{{manifestation}}</scl:jolux>
                    <scl:jolux scl:name="license">{{VerifiedLuxembourgSourceProfile.AdmittingLicence}}</scl:jolux>
                  </scl:JOLUXManifestation>
                </identification></meta>
                <body><article id="art_1" wId="/eli/etat/leg/loi/2026/01/01/a1/art_1">
                  <meta><scl:jolux name="dateApplicability">2026-02-03</scl:jolux></meta>
                  <num>Art. 1.</num><content><p>Indexable publisher words.</p></content>
                </article></body>
              </act>
            </akomaNtoso>
            """);
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompleteXmlForStage3BodyCompositionAsync(xml, store);
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
        Assert.AreEqual(1, reader.ArticleCount);
        var cell = built.CapabilityManifest.Cells.Single();
        Assert.AreEqual("fra", cell.Language);
        Assert.AreEqual(new DateOnly(2026, 2, 3), cell.PeriodFrom);
        Assert.AreEqual(cell.PeriodFrom, cell.PeriodTo);
        Assert.AreEqual(1, cell.Population);
        var search = reader.Search(
            "fra", cell.PeriodFrom, cell.PeriodTo, "Indexable publisher words");
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, search.Outcome);
        Assert.HasCount(1, search.ArticleIdentities);
        Assert.AreEqual(
            envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes
                .Single().Article!.IdentitySha256,
            search.ArticleIdentities.Single());
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
            command.CommandText = "CREATE TABLE invented(value TEXT) STRICT";
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
