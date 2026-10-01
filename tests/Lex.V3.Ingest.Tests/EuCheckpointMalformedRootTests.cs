using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuCheckpointMalformedRootTests
{
    [TestMethod]
    [DataRow("enumeration")]
    [DataRow("expression")]
    [DataRow("tripwire")]
    public async Task LiteralNullIsAnIntegrityRefusalBeforeAnyReplayWrites(string reader)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync("null"u8.ToArray(), CustodyClass.NightlyFloor90d, CancellationToken.None);
        var checkpoint = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
        var expected = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", new string('a', 64));
        var writes = store.WrittenDigestsInOrder.Count;
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(async () =>
        {
            switch (reader)
            {
                case "enumeration":
                    await EuEnumerationCheckpoint.OpenAsync(store, checkpoint, expected, expected, CancellationToken.None);
                    break;
                case "expression":
                    await EuLanguageScopedExpressionProducer.ReopenAsync(store, checkpoint, CancellationToken.None);
                    break;
                case "tripwire":
                    await EuCorrigendumTripwireProducer.ReopenAsync(store, checkpoint, CancellationToken.None);
                    break;
                default:
                    Assert.Fail("Unknown reader fixture.");
                    break;
            }
        });
        Assert.AreEqual(writes, store.WrittenDigestsInOrder.Count);
    }
}
