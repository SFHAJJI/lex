using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgFirstMountAcquisitionTests
{
    [TestMethod]
    public void PopulationScopeCopiesRangesAndRejectsOverlapOrPolicyRelabeling()
    {
        var ranges = LuxembourgPopulationScope.Legislative.Ranges.ToArray();
        var scope = new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy, ranges);
        ranges[0] = ActRange;
        Assert.AreEqual("legislative-code", scope.Ranges[0].Name);
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope("unknown", [ActRange]));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.LegislativePolicy, [ActRange]));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy, []));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy, [ActRange, ActRange]));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy,
            LuxembourgPopulationScope.Legislative.Ranges.Reverse().ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy,
            [new("first", "A", "D"), new("overlap", "C", "E")]));
    }

    [TestMethod]
    public async Task DisjointScopeDoesNotEnumerateObjectsInTheGap()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        using var handler = new LuxembourgFamilyHandler(PdfBytes(), includeSecondWork: true);
        var scope = new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy,
            [LuxembourgPopulationScope.Legislative.Ranges[0], ActRange]);
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var captured = await Acquisition(store, handler).RunPopulationAsync(scope,
            renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(captured.Delivered, captured.Detail);
        Assert.HasCount(6, captured.Run!.FamilyOutcomes);
        Assert.HasCount(1, handler.DocumentRequests, "The second fixture work lies in the excluded gap.");
        Assert.IsFalse(handler.DocumentRequests.Any(uri => uri.Contains("/2025/", StringComparison.Ordinal)));
        var reopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(await CopyVocabularyStoreAsync(store),
            captured.CheckpointRef!, scope, CancellationToken.None);
        Assert.AreEqual(captured.Run.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);
    }

    [TestMethod]
    public async Task LegislativeScopeProvesNineFamiliesAndReopensBothCustodyCopies()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        using var handler = new LuxembourgFamilyHandler(PdfBytes());
        var renderers = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        var captured = await Acquisition(store, handler).RunPopulationAsync(LuxembourgPopulationScope.Legislative,
            renderers, LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
        Assert.IsTrue(captured.Delivered, captured.Detail);
        Assert.HasCount(9, captured.Run!.FamilyOutcomes);
        Assert.IsNotNull(captured.CheckpointRef);
        Assert.IsNotNull(captured.PopulationScopeManifestRef);
        var manifest = await VocabularyRootAsync(store, captured.PopulationScopeManifestRef);
        var expectedFamilies = captured.Profile!.ObservedIriVocabulary.Where(value => value.Kind == LuxembourgVocabularyKind.TypeDocument)
            .Select(value => value.FullIri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expectedFamilies, manifest["publication_families"]!.AsArray()
            .Select(value => value!["family_iri"]!.GetValue<string>()).ToArray());
        Assert.IsTrue(manifest["publication_families"]!.AsArray().All(value =>
            value!["disposition"]!.GetValue<string>() == "not_enumerated_as_independent_type_family"));
        Assert.AreEqual("not_enumerated_outside_launch_uri_scope", manifest["outside_declared_ranges"]!.GetValue<string>());
        foreach (var copy in new[] { await CopyVocabularyStoreAsync(store), await CopyVocabularyStoreAsync(store) })
        {
            var reopened = await LuxembourgFirstMountAcquisition.ReopenPopulationAsync(copy, captured.CheckpointRef,
                LuxembourgPopulationScope.Legislative, CancellationToken.None);
            Assert.AreEqual(captured.PopulationScopeManifestRef, reopened.PopulationScopeManifestRef);
            Assert.AreEqual(captured.Run.CorpusRecordSetRef, reopened.Run!.CorpusRecordSetRef);
            Assert.AreEqual(captured.Run.ObservedObjectIdentitySetRef, reopened.Run.ObservedObjectIdentitySetRef);
            Assert.AreEqual(captured.AknInventory!.IdentitySha256, reopened.AknInventory!.IdentitySha256);
            Assert.AreEqual(captured.AknLegalContent!.IdentitySha256, reopened.AknLegalContent!.IdentitySha256);
        }
        var wrongPolicy = new LuxembourgPopulationScope(LuxembourgPopulationScope.DeclaredRangePolicy,
            LuxembourgPopulationScope.Legislative.Ranges);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenPopulationAsync(
            store, captured.CheckpointRef, wrongPolicy, CancellationToken.None));
        var root = await VocabularyRootAsync(store, captured.CheckpointRef);
        var missingDeclaration = await CopyVocabularyStoreAsync(store, omit: root["scope_definition"]!["sha256"]!.GetValue<string>());
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => LuxembourgFirstMountAcquisition.ReopenPopulationAsync(
            missingDeclaration, captured.CheckpointRef, LuxembourgPopulationScope.Legislative, CancellationToken.None));
        foreach (var changed in new[] { "scope_identity", "inventory", "null_parts", "null_query", "ranges", "manifest" })
        {
            var copy = await CopyVocabularyStoreAsync(store);
            var tampered = await VocabularyRootAsync(copy, captured.CheckpointRef);
            switch (changed)
            {
                case "scope_identity": tampered["scope_definition"]!["resource_id"] = "urn:uuid:00000000-0000-4000-8000-000000000088"; break;
                case "inventory": tampered["parts"]!["inventory_sha256"] = new string('a', 64); break;
                case "null_parts": tampered["parts"] = null; break;
                case "null_query": tampered["parts"]!["query"] = null; break;
                case "manifest": tampered["scope_manifest"]!["sha256"] = tampered["scope_definition"]!["sha256"]!.GetValue<string>(); break;
                case "ranges": tampered["ranges"]![0]!["name"] = "relabelled"; break;
            }
            var held = await copy.CreateAsync(Encoding.UTF8.GetBytes(tampered.ToJsonString()), CustodyClass.NightlyFloor90d, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgFirstMountAcquisition.ReopenPopulationAsync(copy,
                new SourceArtifactRef(captured.CheckpointRef.ResourceId, held.Reference.ContentSha256),
                LuxembourgPopulationScope.Legislative, CancellationToken.None));
        }
    }
}
