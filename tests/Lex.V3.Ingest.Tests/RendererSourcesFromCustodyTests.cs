using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class RendererSourcesFromCustodyTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CopiedRendererSourcesKeepOriginalIdentitiesAndBytesWithoutWrites(bool luxembourg, bool weaker)
    {
        var (original, mapping) = await CaptureAsync(luxembourg);
        var copy = await CopyAsync(original, weaker: weaker);
        var writes = copy.CreateCallCount;
        var expected = mapping.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var reverse = mapping.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var first = await ReopenAsync(luxembourg, copy, reverse);
        reverse.Clear();
        var second = await ReopenAsync(luxembourg, copy, expected);
        var names = Names(luxembourg);
        for (var index = 0; index < names.Count; index++)
        {
            Assert.AreEqual(expected[names[index]], first[index].Reference);
            Assert.AreEqual(first[index].Reference, second[index].Reference);
            CollectionAssert.AreEqual((await original.ReadByDigestAsync(expected[names[index]].Sha256, CancellationToken.None)).ToArray(), first[index].CopyBytes().ToArray());
            CollectionAssert.AreEqual(first[index].CopyBytes().ToArray(), second[index].CopyBytes().ToArray());
        }
        Assert.AreEqual(writes, copy.CreateCallCount);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(false, 3)]
    [DataRow(false, 4)]
    [DataRow(false, 5)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    public async Task EveryNamedRendererBodyMustStillExist(bool luxembourg, int missing)
    {
        var (store, mapping) = await CaptureAsync(luxembourg);
        var copy = await CopyAsync(store, omit: mapping[Names(luxembourg)[missing]].Sha256);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => ReopenAsync(luxembourg, copy, mapping));
    }

    [TestMethod]
    [DataRow(false, "missing")]
    [DataRow(false, "extra")]
    [DataRow(false, "renamed")]
    [DataRow(false, "case")]
    [DataRow(false, "null")]
    [DataRow(true, "missing")]
    [DataRow(true, "extra")]
    [DataRow(true, "renamed")]
    [DataRow(true, "case")]
    [DataRow(true, "null")]
    public async Task TheRoleMappingMustBeExact(bool luxembourg, string changed)
    {
        var (store, mapping) = await CaptureAsync(luxembourg);
        var key = Names(luxembourg)[0];
        var reference = mapping[key];
        switch (changed)
        {
            case "missing": mapping.Remove(key); break;
            case "extra": mapping.Add("unrelated.cs", reference); break;
            case "null": mapping[key] = null!; break;
            default:
                mapping.Remove(key);
                mapping.Add(changed == "case" ? key.ToUpperInvariant() : "unrelated.cs", reference);
                break;
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ReopenAsync(luxembourg, store, mapping));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AChangedDigestDoesNotFallBackToACheckout(bool luxembourg)
    {
        var (store, mapping) = await CaptureAsync(luxembourg);
        var key = Names(luxembourg)[0];
        mapping[key] = new SourceArtifactRef(mapping[key].ResourceId, new string('a',64));
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => ReopenAsync(luxembourg, store, mapping));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationPropagatesWithoutWriting(bool luxembourg)
    {
        var (store, mapping) = await CaptureAsync(luxembourg);
        var writes = store.CreateCallCount;
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ReopenAsync(luxembourg, store, mapping, source.Token));
        Assert.AreEqual(writes, store.CreateCallCount);
    }

    private static IReadOnlyList<string> Names(bool luxembourg) => luxembourg ? LuxembourgRendererSources.RendererFiles : EuRendererSources.RendererFiles;
    private static async Task<MachineQueryRendererSource[]> ReopenAsync(bool luxembourg, ICustodyStore store,
        IReadOnlyDictionary<string,SourceArtifactRef> mapping, CancellationToken token = default)
    {
        if (luxembourg)
        {
            var sources = await LuxembourgRendererSources.FromCustodyAsync(store, mapping, token);
            return [sources.Query, sources.DocumentFetch];
        }
        var europe = await EuRendererSources.FromCustodyAsync(store, mapping, token);
        return [europe.Census, europe.ObjectFacts, europe.Witness, europe.DocumentFetch, europe.FormexManifestation, europe.LegalNotice];
    }
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, Dictionary<string,SourceArtifactRef> Mapping)> CaptureAsync(bool luxembourg)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var mapping = new Dictionary<string,SourceArtifactRef>(StringComparer.Ordinal);
        foreach (var name in Names(luxembourg))
        {
            var bytes = Encoding.UTF8.GetBytes("retained renderer fixture: " + name);
            var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
            mapping.Add(name, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256));
        }
        return (store, mapping);
    }
    private static async Task<EuAcquisitionTestFixture.EuInMemoryCustodyStore> CopyAsync(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore source, string? omit = null, bool weaker = false)
    {
        var copy = new EuAcquisitionTestFixture.EuInMemoryCustodyStore(unenforceDigest: _ => weaker);
        foreach (var digest in source.WrittenDigestsInOrder.Distinct().Where(digest => digest != omit))
            await copy.CreateAsync(await source.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        return copy;
    }
}
