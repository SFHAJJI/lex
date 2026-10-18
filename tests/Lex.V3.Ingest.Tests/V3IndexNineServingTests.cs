using Lex.V3.Api;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The tripwire of the review of #980 (F4): until S7a serves index 9 (its interval rules, and a date's role worded apart,
/// R21), the server refuses a built mount of lex-v3-luxembourg-index/9, one that carries its build report as a release's
/// mount does, with one sentence; a test fixture mount, with no build report, is still served; a mount of schema 8 is
/// served either way. The index-9 mount here is built from the synthetic act of <c>Fixtures/LegacySchema8Mount</c>
/// (no publisher text).
/// </summary>
[TestClass]
public sealed class V3IndexNineServingTests
{
    /// <summary>
    /// The switch is off. This fails when S7a turns it on: update this test, the G5 case of V3ReplayGuaranteesTests (from
    /// the answer alone) and STATUS-DATA.md with it.
    /// </summary>
    [TestMethod]
    public void IndexNineIsNotServedUntilS7aTurnsTheSwitch()
    {
        Assert.IsFalse(V3CorpusMount.ServesIndexNine,
            "S7a serves index 9: update V3IndexNineServingTests, G5 from the answer alone in V3ReplayGuaranteesTests, and STATUS-DATA.md");
    }

    [TestMethod]
    public async Task ABuiltMountOfIndexNineIsRefusedWithItsSentenceAndAFixtureMountIsServed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-index9-serving-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await WriteSyntheticIndexNineMountAsync(directory);

            // A fixture mount (no build report): served.
            using (var fixture = V3CorpusMount.Served(await V3CorpusMount.OpenAsync(directory, CancellationToken.None), directory))
            {
                Assert.IsNotNull(fixture);
            }

            // The same files as a built mount: refused, with the sentence.
            await File.WriteAllTextAsync(Path.Combine(directory, V3CorpusMount.BuildReportFileName), "{}");
            var opened = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => V3CorpusMount.Served(opened, directory));
            Assert.AreEqual(V3CorpusMount.IndexNineNotServedSentence, refused.Message);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }

        // A built mount of schema 8 is served.
        var eight = Path.Combine(Path.GetTempPath(), $"lex-v3-index8-serving-{Guid.NewGuid():N}");
        Directory.CreateDirectory(eight);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(eight, V3CorpusMount.CorpusFileName), V3CorpusLegacySchema8MountTests.Fixture("lex-corpus-6.json.bin"));
            await File.WriteAllBytesAsync(Path.Combine(eight, V3CorpusMount.IndexFileName), V3CorpusLegacySchema8MountTests.Fixture("luxembourg-index.sqlite3.bin"));
            await File.WriteAllBytesAsync(Path.Combine(eight, V3CorpusMount.CapabilityManifestFileName), V3CorpusLegacySchema8MountTests.Fixture("luxembourg-capability-manifest.json.bin"));
            await File.WriteAllTextAsync(Path.Combine(eight, V3CorpusMount.BuildReportFileName), "{}");
            using var served = V3CorpusMount.Served(await V3CorpusMount.OpenAsync(eight, CancellationToken.None), eight);
            Assert.IsNotNull(served);
        }
        finally
        {
            try { Directory.Delete(eight, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The synthetic act run through the acquisition fixture and built by this code: an index 9 mount.</summary>
    private static async Task WriteSyntheticIndexNineMountAsync(string directory)
    {
        const string manifestation = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
        const string item = "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml/eli-etat-leg-loi-2026-01-01-a1-jo-fr-xml.xml";
        var xml = V3CorpusLegacySchema8MountTests.Fixture("synthetic-loi-2026-01-01-a1.xml");
        ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        var luxembourg = await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(luxembourgOverride: luxembourg, luxembourgStore: store);
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var index = LuxembourgIndexBuilder.TryBuild(envelope, LuxembourgIndexBuilderTests.BuiltAt, out var indexRefusal, out var indexDetail);
        Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CorpusFileName), corpus.CanonicalBytes.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.IndexFileName), index.IndexBytes.ToArray());
        await File.WriteAllBytesAsync(Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName), index.CapabilityManifestBytes.ToArray());
    }
}
