using System.IO.Compression;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

public sealed partial class EuFirstMountAcquisitionTests
{
    internal const string ConsolidatedSeed = "32016R0679";
    internal const string ConsolidatedWork = "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";

    [TestMethod]
    public async Task OriginalAndConsolidatedEnglishAndFrenchPackagesAreAcquiredAndReplay()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var result = await AcquireConsolidatedAsync(store);
        Assert.IsTrue(result.Delivered, result.Detail);
        Assert.AreEqual(4, result.Formex!.AcquiredExpressionCount);
        Assert.HasCount(2, result.Run!.ObservedWorkFacts);
        Assert.IsTrue(result.Run.ObservedWorkFacts.All(work => work.CensusInterpretationProfileRef is not null));
        var states = EuropeIndexBuilder.ProjectStates(result.Run.ObservedWorkFacts);
        var consolidated = states.Single(state => state.PublisherWorkIri == ConsolidatedWork);
        Assert.AreEqual("02016R0679-20240101", consolidated.PublisherWorkCelex);
        Assert.AreEqual("2024-01-01", consolidated.PublisherConsolidationDate);
        var replay = await EuFirstMountAcquisition.ReopenAsync(store, result.CheckpointRef!, [ConsolidatedSeed], CancellationToken.None);
        Assert.IsTrue(replay.Delivered, replay.Detail);
        Assert.AreEqual(4, replay.Formex!.AcquiredExpressionCount);
        CollectionAssert.AreEqual(states, EuropeIndexBuilder.ProjectStates(replay.Run!.ObservedWorkFacts));
    }

    internal static async Task<EuFirstMountAcquisitionResult> AcquireConsolidatedAsync(ICustodyStore store)
    {
        var root = EuAxiomWiringHarness.SeedRoot(ConsolidatedSeed);
        var works = new[] { root, ConsolidatedWork }.Order(StringComparer.Ordinal).ToArray();
        var scripts = EuAxiomWiringHarness.Scripts(root, work => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(work));
        var census = new[] { EuAcquisitionTestFixture.CensusFamilyRow(ConsolidatedSeed, root, ConsolidatedWork) };
        scripts["Census"] = EuAcquisitionTestFixture.ScriptFor("Census", 1, census, EuAcquisitionTestFixture.CensusFamilyProjection);
        var p = works.SelectMany(work => EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Concat(EuAcquisitionTestFixture.RelationPredicates).Order(StringComparer.Ordinal).Select(predicate =>
            {
                if (predicate == EuAcquisitionTestFixture.ResourceLegalIdCelex)
                    return EuAcquisitionTestFixture.ObjectFactLiteralRow(work, predicate,
                        work == root ? ConsolidatedSeed : "02016R0679-20240101", "http://www.w3.org/2001/XMLSchema#string");
                if (predicate == EuAcquisitionTestFixture.ActConsolidatedDate && work != root)
                    return EuAcquisitionTestFixture.ObjectFactLiteralRow(work, predicate, "2024-01-01", "http://www.w3.org/2001/XMLSchema#date");
                var value = predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? work == root ? EuAcquisitionTestFixture.RegulationResourceType : "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT"
                    : predicate == EuAcquisitionTestFixture.ConsolidatedBasedOnPredicate && work != root ? root : null;
                return EuAcquisitionTestFixture.ObjectFactRow(work, predicate, value);
            })).ToArray();
        scripts["P"] = EuAcquisitionTestFixture.ScriptFor("P", p.Length, p, EuAcquisitionTestFixture.ObjectFactsProjection);
        var x = works.SelectMany(work => new[] { (Suffix: ".0001", Language: "ENG"), (Suffix: ".0002", Language: "FRA") }
            .SelectMany(language => new[]
            {
                EuAcquisitionTestFixture.ExpressionFactRow(work, work + language.Suffix),
                EuAcquisitionTestFixture.ExpressionLanguageRow(work, work + language.Suffix,
                    "http://publications.europa.eu/resource/authority/language/" + language.Language),
            })).ToArray();
        scripts["X"] = EuAcquisitionTestFixture.ScriptFor("X", x.Length, x, EuAcquisitionTestFixture.ExpressionFactsProjection);
        var m = works.SelectMany(work => EuAcquisitionTestFixture.RealBandListedTypes.Order(StringComparer.Ordinal)
            .Select(type => EuAcquisitionTestFixture.ManifestationFactsRow(work, type))).ToArray();
        scripts["M"] = EuAcquisitionTestFixture.ScriptFor("M", m.Length, m, EuAcquisitionTestFixture.ManifestationFactsProjection);
        scripts["A"] = EuAcquisitionTestFixture.AxiomAbsenceScriptFor(works);
        scripts["L"] = EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor(works);
        var english = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));
        var french = SyntheticFrenchPackage(english);
        var handler = new CompositeHandler(scripts, works.SelectMany(work => new[] { work + ".0001", work + ".0002" })
            .ToDictionary(expression => expression, _ => new[] { "fmx4" }, StringComparer.Ordinal),
            packageBody: request => request.RequestUri!.AbsolutePath.Contains(".0002.", StringComparison.Ordinal) ? french : english);
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        return await Acquisition(store, handler).RunAsync([ConsolidatedSeed], renderers,
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
    }

    // A synthetic language-binding fixture, not a claimed French legal text. The held publisher
    // ZIP supplies structure; only language coordinates and file references change for this test.
    private static byte[] SyntheticFrenchPackage(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName.Replace("EN", "FR", StringComparison.Ordinal));
                copy.LastWriteTime = entry.LastWriteTime;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                using var writer = new StreamWriter(copy.Open(), new UTF8Encoding(false));
                writer.Write(reader.ReadToEnd().Replace("2016119EN", "2016119FR", StringComparison.Ordinal)
                    .Replace(">EN<", ">FR<", StringComparison.Ordinal));
            }
        return output.ToArray();
    }
}
