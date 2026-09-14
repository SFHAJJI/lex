using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuXhtmlAnnexInventoryProducerTests
{
    [TestMethod]
    public async Task RetainedXhtmlBindsPublisherAnnexToItsWorkAndFormexUnit()
    {
        var bytes = await FixtureBytesAsync("new-xhtml-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Inventory);
        Assert.AreEqual(receipt, result.Inventory.SourceReceipt);
        Assert.AreEqual(profile.Reference, result.Inventory.ProfileRef);
        Assert.AreEqual("http://data.europa.eu/eli/reg_impl/2026/1965/oj", result.Inventory.WorkEli);
        Assert.AreEqual(1, result.Inventory.Members.Count);
        Assert.AreEqual("L_202601965EN.000201.fmx", result.Inventory.Members[0].PublisherUnitId);
        Assert.AreEqual("L_202601965EN.000201.fmx.xml", result.Inventory.Members[0].FormexPackageEntry);
        Assert.AreEqual("anx_1", result.Inventory.Members[0].PublisherAnnexId);
        Assert.AreEqual("ANNEX", result.Inventory.Members[0].Title);
    }

    [TestMethod]
    public async Task RetainedXhtmlWithoutThePublisherUnitConventionIsATypedGap()
    {
        var bytes = await FixtureBytesAsync("gdpr-xhtml-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionAbsent, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task RepeatingTheSameEvidenceProducesTheSameInventoryIdentity()
    {
        var bytes = await FixtureBytesAsync("new-xhtml-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);
        var producer = new EuXhtmlAnnexInventoryProducer(store);

        var first = await producer.RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);
        var second = await producer.RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.IsNotNull(first.Inventory);
        Assert.IsNotNull(second.Inventory);
        Assert.AreEqual(first.Inventory.IdentitySha256, second.Inventory.IdentitySha256);
    }

    [TestMethod]
    public async Task ProfileAndCustodySubstitutionCannotProduceAnInventory()
    {
        var bytes = await FixtureBytesAsync("new-xhtml-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);
        var wrongProfile = Profile(new string('a', 64));
        var wrongReference = new SourceArtifactRef(profile.Reference.ResourceId, new string('b', 64));
        var producer = new EuXhtmlAnnexInventoryProducer(store);

        var wrongTransport = await producer.RunAsync(
            receipt, wrongProfile.Bytes, wrongProfile.Reference, CancellationToken.None);
        var wrongDigest = await producer.RunAsync(
            receipt, profile.Bytes, wrongReference, CancellationToken.None);

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.ProfileDoesNotNameTransport, wrongTransport.Refusal);
        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.ProfileDigestMismatch, wrongDigest.Refusal);

        var heldBytes = (Dictionary<string, byte[]>)typeof(EuAcquisitionTestFixture.EuInMemoryCustodyStore)
            .GetField("_byDigest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        heldBytes[receipt.Reference.ContentSha256] = "substituted"u8.ToArray();
        var substituted = await producer.RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);
        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.RetainedBytesUnavailable, substituted.Refusal);
    }

    [TestMethod]
    public async Task InternalDocumentTypeSubsetIsRefused()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html [<!ELEMENT html ANY>]>
            <html xmlns="http://www.w3.org/1999/xhtml"><body/></html>
            """);
        var (store, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task MalformedPublisherAnnexIdentifierIsRefused()
    {
        var result = await RunAsync(Xhtml("anx_not-a-number", includeWorkEli: true));

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task MultipleAnnexContainersInOnePublisherUnitAreRefused()
    {
        var result = await RunAsync(Xhtml(
            "anx_1", includeWorkEli: true, secondAnnexId: "anx_2"));

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task MalformedPublisherUnitIdentifierIsRefused()
    {
        var result = await RunAsync(Xhtml(
            "anx_1", includeWorkEli: true, publisherUnitId: "malformed/unit.fmx"));

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task PublisherAnnexWithoutAWorkEliIsRefused()
    {
        var result = await RunAsync(Xhtml("anx_1", includeWorkEli: false));

        Assert.AreEqual(EuXhtmlAnnexInventoryRefusal.WorkEliMissing, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    private static async Task<EuXhtmlAnnexInventoryProductionResult> RunAsync(string xhtml)
    {
        var (store, receipt, profile) = await FixtureAsync(Encoding.UTF8.GetBytes(xhtml));
        return await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);
    }

    private static string Xhtml(
        string annexId,
        bool includeWorkEli,
        string publisherUnitId = "L_202601965EN.000201.fmx",
        string? secondAnnexId = null)
    {
        var secondAnnex = secondAnnexId is null
            ? string.Empty
            : $"""
              <div class="eli-container" id="{secondAnnexId}">
                <p class="oj-doc-ti">SECOND ANNEX</p>
              </div>
              """;
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body>
                <div id="{{publisherUnitId}}">
                  <div class="eli-container" id="{{annexId}}">
                    <p class="oj-doc-ti">ANNEX</p>
                  </div>
                  {{secondAnnex}}
                </div>
                {{(includeWorkEli ? "<p>ELI: http://data.europa.eu/eli/reg_impl/2026/1965/oj</p>" : string.Empty)}}
              </body>
            </html>
            """;
    }

    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        DurableBlobWriteReceipt Receipt, (byte[] Bytes, SourceArtifactRef Reference) Profile)>
        FixtureAsync(byte[] bytes)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var profile = Profile(receipt.Reference.ContentSha256);
        return (store, receipt, profile);
    }

    private static (byte[] Bytes, SourceArtifactRef Reference) Profile(string transportSha256)
    {
        var profileBytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-xhtml-annex-inventory-profile/1",
            "transport_sha256=" + transportSha256,
            "xhtml_namespace=http://www.w3.org/1999/xhtml") + "\n");
        var profileRef = new SourceArtifactRef(
            "urn:uuid:99999999-9999-4999-8999-999999999999",
            Convert.ToHexStringLower(SHA256.HashData(profileBytes)));
        return (profileBytes, profileRef);
    }

    private static async Task<byte[]> FixtureBytesAsync(string name) => await File.ReadAllBytesAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name));
}
