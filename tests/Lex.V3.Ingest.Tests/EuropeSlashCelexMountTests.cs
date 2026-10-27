using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Ingest.Europe;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// An EU act whose CELEX carries a solidus (the Treaties' and the Charter's, served from main-body profile /5 on) through the
/// API, against the GDPR's (review of #996, F1): the CELEX is one path segment of every address the API mints, its solidus
/// written <c>%2F</c>, and every address it mints is one it reads back, while a CELEX with no solidus keeps its bytes.
/// </summary>
public sealed partial class EuFormexPackagePopulationProducerTests
{
    [TestMethod]
    [DataRow("12016P/TXT")]
    [DataRow("12016E/TXT")]
    [DataRow("32016R0679")]
    public async Task AnEuActWhoseCelexCarriesASolidusIsServedAndItsAddressesReadBack(string seed)
    {
        var (result, _, english, store) = await AcquireEnglishAsync(_ => null, seedCelex: seed);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
            europeOverride: result.Reconciliation!.Run, formexOverride: result.Reconciliation, formexStore: store);
        var mainBody = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
            .Single(value => value.Source.ExpressionIdentity == english.Identity);
        Assert.AreEqual(EuFormexMainBodyLegalContentDisposition.Admitted, mainBody.Disposition, mainBody.Detail);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(corpus, $"{refusal}: {detail}");
        var index = EuropeIndexBuilder.TryBuild(envelope, out var indexRefusal, out var indexDetail);
        Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");

        var segment = seed.Replace("/", "%2F", StringComparison.Ordinal);
        Assert.AreEqual(segment, V3CorpusMount.EuropeWorkSegment(seed));
        Assert.AreEqual(seed, V3CorpusMount.EuropeCelexOfSegment(segment));
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-slash-celex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName), index.IndexBytes.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName), index.CapabilityManifestBytes.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus.CanonicalBytes.ToArray());
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);

            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", new { identifier = seed });
            Assert.IsNull(dossier.Refusal, dossier.Refusal?.Code);
            var pinned = dossier.Result!.Value.GetProperty("expressions").EnumerateArray().First().GetProperty("pinned_wording");
            var date = pinned.GetProperty("wording_date").GetString()!;
            var permalink = pinned.GetProperty("permalink").GetString()!;
            StringAssert.StartsWith(permalink, $"/eu-eurlex/{segment}/{date}--", "the CELEX is one segment of the permalink (R64)");
            Assert.HasCount(5, permalink.Split('/'), permalink);

            var byCelex = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = seed, date, language = "eng" });
            Assert.IsNull(byCelex.Refusal, byCelex.Refusal?.Code);
            var wording = byCelex.Result!.Value.GetProperty("wordings").EnumerateArray().Single();
            Assert.AreEqual(permalink, wording.GetProperty("permalink").GetString());
            Assert.AreEqual($"/eu-eurlex/{segment}/eng/{date}", wording.GetProperty("stable_coordinate").GetString());
            var article = wording.GetProperty("articles").EnumerateArray().First();
            var articlePermalink = article.GetProperty("article_permalink").GetString()!;
            StringAssert.StartsWith(articlePermalink, permalink + "/");

            var byArticle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle",
                new { identifier = mainBody.Articles[0].IdentitySha256, date, language = "eng" });
            Assert.IsNull(byArticle.Refusal, byArticle.Refusal?.Code);
            Assert.AreEqual(permalink, byArticle.Result!.Value.GetProperty("wordings").EnumerateArray().Single().GetProperty("permalink").GetString());

            var byPermalink = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = permalink, date, language = "eng" });
            Assert.IsNull(byPermalink.Refusal, byPermalink.Refusal?.Code);

            foreach (var address in new[] { permalink, articlePermalink })
            {
                var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = address });
                Assert.IsNull(verified.Refusal, $"{address}: {verified.Refusal?.Code}");
            }

            // Every address of the act this site mints, typed into search, opens the act (R64's reader): its permalinks, its
            // address, its readable address and its stable coordinate, each naming the CELEX as one segment.
            foreach (var address in new[] { permalink, articlePermalink, $"/eu-eurlex/{segment}", $"/eu-eurlex/{segment}/eng", $"/eu-eurlex/{segment}/eng/{date}" })
            {
                var opened = V3CorpusReferenceMountTests.Opened(await V3CorpusReferenceMountTests.SearchAsync(mount, address, language: "eng"), address);
                StringAssert.StartsWith(opened.GetProperty("opens").GetProperty("address").GetString(), $"/eu-eurlex/{segment}", address);
            }

            // The time view answers the act (or refuses it typed) and never fails on its CELEX: as_of built the stable
            // coordinate from the permalink's segments, and a solidus in the CELEX broke them.
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = seed, date, language = "eng" });
            var asOfText = asOf.Result?.Value.GetRawText() ?? asOf.Refusal!.Code;
            Assert.IsFalse(asOfText.Contains($"/eu-eurlex/{seed}/", StringComparison.Ordinal) && seed.Contains('/', StringComparison.Ordinal),
                "no address names a raw solidus");
            if (asOf.Result is { } asOfResult && asOfResult.Value.TryGetProperty("states", out var states))
            {
                foreach (var state in states.EnumerateArray())
                {
                    if (state.TryGetProperty("wording", out var held) && held.ValueKind == JsonValueKind.Object &&
                        held.TryGetProperty("permalink", out var heldPermalink) && heldPermalink.ValueKind == JsonValueKind.String)
                        StringAssert.StartsWith(heldPermalink.GetString(), $"/eu-eurlex/{segment}/");
                }
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
