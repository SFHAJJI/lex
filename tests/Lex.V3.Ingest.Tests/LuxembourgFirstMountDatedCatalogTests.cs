using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The dated Luxembourg catalogs (<c>lex-lu-first-mount-acquisition/5</c> for an act range, <c>/6</c> for a population): a run
/// that was not resumed wraps the record it always wrote with the instant it retained it, the reopen carries that instant, and
/// the catalogs written before (<c>/1</c> and <c>/2</c>, undated) reopen exactly as they always did.
/// </summary>
public sealed partial class LuxembourgFirstMountAcquisitionTests
{
    private const string DatedActSchema = "lex-lu-first-mount-acquisition/5";
    private const string DatedPopulationSchema = "lex-lu-first-mount-acquisition/6";

    /// <summary>The record a dated catalog wraps (<paramref name="member"/>: <c>acquisition</c> or <c>population</c>).</summary>
    internal static JsonNode DatedRecord(JsonNode catalog, string member)
    {
        CollectionAssert.Contains(new[] { DatedActSchema, DatedPopulationSchema }, catalog["schema"]!.GetValue<string>(),
            "a run that was not resumed writes a dated catalog");
        Assert.IsNotNull(catalog["cataloged_at"], "a dated catalog states its instant");
        return catalog[member]!;
    }

    [TestMethod]
    public async Task AFreshActRunRetainsADatedCatalogWhoseReopenStatesTheSameInstant()
    {
        var capture = await CapturedVocabulary.Value;
        Assert.IsNull(capture.Result.Resumption);
        Assert.IsNotNull(capture.Result.CatalogedAt);
        var catalog = await VocabularyRootAsync(capture.Store, capture.Result.CheckpointRef!);
        Assert.AreEqual(DatedActSchema, catalog["schema"]!.GetValue<string>());
        Assert.AreEqual("lex-lu-first-mount-acquisition/1", DatedRecord(catalog, "acquisition")["schema"]!.GetValue<string>(),
            "the wrapped record is the one an undated run wrote");
        Assert.AreEqual(capture.Result.CatalogedAt,
            DateTimeOffset.Parse(catalog["cataloged_at"]!.GetValue<string>(), CultureInfo.InvariantCulture));

        var copy = await CopyVocabularyStoreAsync(capture.Store);
        var reopened = await LuxembourgFirstMountAcquisition.ReopenAsync(copy, capture.Result.CheckpointRef!, ActRange, CancellationToken.None);
        Assert.AreEqual(capture.Result.CatalogedAt, reopened.CatalogedAt);
        Assert.IsNull(reopened.Resumption);
        Assert.AreEqual(capture.Result.CheckpointRef!.Sha256, (await HoldJsonAsync(copy, catalog)).Sha256,
            "a dated catalog round-trips to its own canonical bytes, so the refusals below are the changes'");

        // The record alone is the undated catalog /1 of the same acquisition: it reopens as it always did, stating no time.
        var undated = await HoldJsonAsync(copy, DatedRecord(catalog, "acquisition"));
        var undatedReopened = await LuxembourgFirstMountAcquisition.ReopenAsync(copy, undated, ActRange, CancellationToken.None);
        Assert.IsTrue(undatedReopened.Delivered, undatedReopened.Detail);
        Assert.IsNull(undatedReopened.CatalogedAt);
        Assert.IsNull(undatedReopened.Resumption);
        Assert.AreEqual(capture.Result.Run!.CorpusRecordSetRef, undatedReopened.Run!.CorpusRecordSetRef);
    }

    [TestMethod]
    [DataRow("time_removed")]
    [DataRow("labelled_resumed")]
    [DataRow("labelled_population")]
    public async Task ADatedLuCatalogCannotLoseItsTimeOrBeRelabelled(string changed)
    {
        var capture = await CapturedVocabulary.Value;
        var copy = await CopyVocabularyStoreAsync(capture.Store);
        var catalog = (JsonObject)await VocabularyRootAsync(copy, capture.Result.CheckpointRef!);
        switch (changed)
        {
            case "time_removed": catalog.Remove("cataloged_at"); break;
            case "labelled_resumed": catalog["schema"] = "lex-lu-first-mount-acquisition/3"; break;
            case "labelled_population": catalog["schema"] = DatedPopulationSchema; break;
        }

        var held = await HoldJsonAsync(copy, catalog);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            LuxembourgFirstMountAcquisition.ReopenAsync(copy, held, ActRange, CancellationToken.None));
    }

    [TestMethod]
    public async Task AFreshPopulationRunRetainsADatedCatalogAndItsUndatedRecordReopensAsBefore()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        using var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var captured = await Acquisition(store, handler).RunPopulationAsync(LuxembourgPopulationScope.Legislative,
            renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(captured.Delivered, captured.Detail);
        Assert.IsNotNull(captured.CatalogedAt);
        var catalog = await VocabularyRootAsync(store, captured.CheckpointRef!);
        Assert.AreEqual(DatedPopulationSchema, catalog["schema"]!.GetValue<string>());
        Assert.AreEqual("lex-lu-first-mount-acquisition/2", DatedRecord(catalog, "population")["schema"]!.GetValue<string>());

        var copy = await CopyVocabularyStoreAsync(store);
        var reopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(copy, captured.CheckpointRef!,
            LuxembourgPopulationScope.Legislative, CancellationToken.None);
        Assert.AreEqual(captured.CatalogedAt, reopened.CatalogedAt, "the scope manifest's copy keeps the time too");
        Assert.AreEqual(captured.PopulationScopeManifestRef, reopened.PopulationScopeManifestRef);

        var undated = await HoldJsonAsync(copy, DatedRecord(catalog, "population"));
        var undatedReopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(copy, undated,
            LuxembourgPopulationScope.Legislative, CancellationToken.None);
        Assert.IsNull(undatedReopened.CatalogedAt);
        Assert.AreEqual(captured.Run!.CorpusRecordSetRef, undatedReopened.Run!.CorpusRecordSetRef);
    }

    [TestMethod]
    public void TheScopeWithTheConstitutionDeclaresFourOrderedRangesAndLegislativeIsUnchanged()
    {
        var scope = LuxembourgPopulationScope.LegislativeWithConstitution;
        Assert.AreEqual(LuxembourgPopulationScope.DeclaredRangePolicy, scope.Policy);
        const string Legislation = "http://data.legilux.public.lu/eli/etat/leg/";
        CollectionAssert.AreEqual(
            new[]
            {
                ("legislative-code", Legislation + "code/", Legislation + "code0"),
                ("legislative-constitution", Legislation + "constitution/", Legislation + "constitution0"),
                ("legislative-loi", Legislation + "loi/", Legislation + "loi0"),
                ("legislative-rgd", Legislation + "rgd/", Legislation + "rgd0"),
            },
            scope.Ranges.Select(static range => (range.Name, range.StartInclusive, range.EndExclusive)).ToArray());

        // The Constitution's works, states and expressions fall inside its range and no other; the 1868 revision act is a loi.
        static int RangeOf(LuxembourgPopulationScope declared, string iri) => declared.Ranges.ToList().FindIndex(range =>
            string.CompareOrdinal(range.StartInclusive, iri) <= 0 && string.CompareOrdinal(iri, range.EndExclusive) < 0);
        Assert.AreEqual(1, RangeOf(scope, Legislation + "constitution/1868/10/17/n1/jo"));
        Assert.AreEqual(1, RangeOf(scope, Legislation + "constitution/1868/10/17/n1/consolide/20231001"));
        Assert.AreEqual(2, RangeOf(scope, Legislation + "loi/1868/10/17/n1/jo"));
        Assert.AreEqual(-1, RangeOf(scope, Legislation + "rgd0"));

        // legislative keeps its own policy and exactly its three ranges, so run 15's catalog and inputs still reopen.
        Assert.AreEqual(LuxembourgPopulationScope.LegislativePolicy, LuxembourgPopulationScope.Legislative.Policy);
        CollectionAssert.AreEqual(new[] { "legislative-code", "legislative-loi", "legislative-rgd" },
            LuxembourgPopulationScope.Legislative.Ranges.Select(static range => range.Name).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.LegislativePolicy, scope.Ranges));
    }

    private static async Task<SourceArtifactRef> HoldJsonAsync(ICustodyStore store, JsonNode node)
    {
        var held = await store.CreateAsync(Encoding.UTF8.GetBytes(EuFirstMountAcquisitionTests.CanonicalJson(node)),
            CustodyClass.NightlyFloor90d, CancellationToken.None);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held.Reference.ContentSha256);
    }
}
