using System.Net;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Ingest.Europe;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The parts of a newly served EU act that are not served as text, through the API (review of #996, F2): an admitted package's
/// annexes when its annex chain did not close, and a consolidated Treaty's Protocols, Annexes, Declarations and tables. Each is
/// a typed row of <c>annexes_not_served</c>, on the dossier and on the reading, with its count, identities, reason and official
/// source, so that no reader takes the act for one without them.
/// </summary>
public sealed partial class EuFormexPackagePopulationProducerTests
{
    [TestMethod]
    [DataRow("annex_unclassified")]
    [DataRow("treaty_part_not_served")]
    public async Task AnAdmittedActsPartsNotServedAreTypedRowsOfTheDossierAndTheReading(string disposition)
    {
        var annexChain = disposition == "annex_unclassified";
        var package = annexChain
            ? await FixtureAsync("new-fmx4-200-body.bin")
            : EuFormexMainBodyLegalContentProducerTests.RewriteConsolidated(
                await EuFormexMainBodyLegalContentProducerTests.ReadConsolidatedPackage("eng"),
                static root => EuFormexMainBodyLegalContentProducerTests.AsConsolidatedTreaty(root, "in-part"));
        var (result, _, english, store) = await AcquireEnglishAsync(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/zip", StringComparison.Ordinal)
                    ? EuAcquisitionTestFixture.BinaryResponse(request, HttpStatusCode.OK, package, "application/zip")
                    : null,
            seedCelex: annexChain ? "32016R0679" : "12016E/TXT");
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: result.Reconciliation!.Run, formexOverride: result.Reconciliation, formexStore: store);
        var mainBody = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(value => value.Source.ExpressionIdentity == english.Identity);
        Assert.IsTrue(EuFormexMainBodyLegalContentProducer.CarriesArticles(mainBody.Disposition), mainBody.Detail);
        var part = mainBody.PartsNotServed.Single();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(corpus, $"{refusal}: {detail}");
        var member = corpus.VerifiedSet.Set.Members.Single(value => value.Stage3Outcomes.Any(outcome =>
            outcome.SemanticIdentitySha256 == mainBody.SemanticIdentitySha256));
        var typed = member.Stage3Outcomes.Single(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody);
        Assert.AreEqual(part.IdentitySha256, typed.SemanticIdentitySha256);
        Assert.AreEqual(disposition, Lex.V3.Contracts.ContractWire.NameOf(typed.Disposition));
        var index = EuropeIndexBuilder.TryBuild(envelope, out var indexRefusal, out var indexDetail);
        Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");

        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-parts-not-served-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), index.IndexBytes.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName), index.CapabilityManifestBytes.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus.CanonicalBytes.ToArray());
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var seed = annexChain ? "32016R0679" : "12016E/TXT";
            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = seed });
            Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
            var expression = dossier.Result!.Value.GetProperty("expressions").EnumerateArray()
                .Single(value => value.GetRawText().Contains(english.Identity.PublisherExpressionId, StringComparison.Ordinal));
            AssertRow(expression.GetProperty("annexes_not_served"));
            var wordingDate = expression.GetProperty("pinned_wording").GetProperty("wording_date").GetString()!;
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = seed, date = wordingDate, language = "eng" });
            Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
            var wording = bundle.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
            Assert.IsGreaterThan(0, wording.GetProperty("articles").GetArrayLength());
            AssertRow(wording.GetProperty("annexes_not_served"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void AssertRow(JsonElement rows)
        {
            var row = rows.EnumerateArray().Single();
            Assert.AreEqual(disposition, row.GetProperty("disposition").GetString());
            Assert.AreEqual(1, row.GetProperty("annexes").GetInt32());
            Assert.AreEqual(part.IdentitySha256, row.GetProperty("annex_identities_sha256").EnumerateArray().Single().GetString());
            Assert.AreEqual("text_not_available", row.GetProperty("served_as").GetString());
            Assert.AreEqual(V3CorpusMount.EuropeAnnexReason(typed.Disposition), row.GetProperty("reason").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.GetProperty("official_source").GetString()));
        }
    }
}
