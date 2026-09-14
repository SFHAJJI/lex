using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexAnnexInventoryProducerTests
{
    [TestMethod]
    public async Task RetainedFormexPackageDerivesItsAnnexPopulation()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Inventory);
        Assert.AreEqual(receipt, result.Inventory.SourceReceipt);
        Assert.AreEqual(profile.Reference, result.Inventory.ProfileRef);
        Assert.AreEqual(1, result.Inventory.Members.Count);
        var annex = result.Inventory.Members[0];
        Assert.AreEqual("L_202601965EN.000201.fmx.xml", annex.PackageEntry);
        Assert.AreEqual("0001.0001", annex.Sequence);
        Assert.AreEqual("L_202601965EN.doc.fmx.xml", annex.DocumentReferenceFile);
        Assert.AreEqual("LEU20261965EN1101", annex.DocumentReferenceValue);
        Assert.AreEqual(2, annex.PageFirst);
        Assert.AreEqual(7, annex.PageLast);
        Assert.AreEqual(6, annex.PageTotal);
        Assert.AreEqual("ANNEX", annex.Title);
    }

    [TestMethod]
    public async Task RetainedPackageWithoutAnAnnexProvesZero()
    {
        var bytes = await FixtureBytesAsync("gdpr-fmx4-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Inventory);
        Assert.AreEqual(0, result.Inventory.Members.Count);
    }

    [TestMethod]
    public async Task RepeatingTheSameEvidenceProducesTheSameInventoryIdentity()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);
        var producer = new EuFormexAnnexInventoryProducer(store);

        var first = await producer.RunAsync(receipt, profile.Bytes, profile.Reference, CancellationToken.None);
        var second = await producer.RunAsync(receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.IsNotNull(first.Inventory);
        Assert.IsNotNull(second.Inventory);
        Assert.AreEqual(first.Inventory.IdentitySha256, second.Inventory.IdentitySha256);
    }

    [TestMethod]
    public async Task ProfileBytesBindTheRetainedPackage()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);
        var wrongProfile = Profile(new string('a', 64));
        var wrongReference = new SourceArtifactRef(profile.Reference.ResourceId, new string('b', 64));
        var producer = new EuFormexAnnexInventoryProducer(store);

        var wrongTransport = await producer.RunAsync(
            receipt, wrongProfile.Bytes, wrongProfile.Reference, CancellationToken.None);
        var wrongDigest = await producer.RunAsync(
            receipt, profile.Bytes, wrongReference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.ProfileDoesNotNameTransport, wrongTransport.Refusal);
        Assert.AreEqual(EuFormexAnnexInventoryRefusal.ProfileDigestMismatch, wrongDigest.Refusal);
    }

    [TestMethod]
    public async Task MissingCustodyBytesCannotProduceAnInventory()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (_, receipt, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).RunAsync(
                receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.RetainedBytesUnavailable, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task SubstitutedCustodyBytesCannotProduceAnInventory()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, profile) = await FixtureAsync(bytes);
        var substituted = bytes.ToArray();
        substituted[^1] ^= 1;
        var heldBytes = (Dictionary<string, byte[]>)typeof(EuAcquisitionTestFixture.EuInMemoryCustodyStore)
            .GetField("_byDigest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        heldBytes[receipt.Reference.ContentSha256] = substituted;

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
                receipt, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.RetainedBytesUnavailable, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task NonZipBytesCannotProveAnAnnexPopulation()
    {
        var result = await RunPackageAsync("not a ZIP package"u8.ToArray());

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.PackageUnreadable, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task MalformedXmlCannotProveAnAnnexPopulation()
    {
        var result = await RunPackageAsync(Package(("broken.xml", "<ANNEX>")));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.XmlInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task DuplicateAnnexIdentityIsRefused()
    {
        var annex = AnnexXml("annex.xml", "0001.0001", 2, 7, 6);
        var result = await RunPackageAsync(Package(
            ("annex.xml", DocumentXml()), ("one.xml", annex), ("two.xml", annex)));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.DuplicateAnnexIdentity, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task ContradictoryPageExtentIsRefused()
    {
        var result = await RunPackageAsync(Package(
            ("document.xml", DocumentXml()),
            ("annex.xml", AnnexXml("document.xml", "0001.0001", 2, 7, 5))));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.AnnexPageExtentContradictory, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task AnAnnexWithoutItsPublisherTitleIsRefused()
    {
        var result = await RunPackageAsync(Package(
            ("document.xml", DocumentXml()),
            ("annex.xml", AnnexXml("document.xml", "0001.0001", 2, 7, 6, title: ""))));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.AnnexTitleMissing, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task AnAnnexWhoseDocumentMemberIsAbsentCannotProveAPopulation()
    {
        var result = await RunPackageAsync(Package(
            ("document.xml", DocumentXml()),
            ("annex.xml", AnnexXml("missing-document.xml", "0001.0001", 2, 7, 6))));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.AnnexDocumentReferenceMissing, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task AnEmptyZipCannotMasqueradeAsAProvedZero()
    {
        var result = await RunPackageAsync(Package());

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.PackageDoesNotIdentifyFormex, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task XmlElementNamesWithoutAFormexSchemaCannotProveZero()
    {
        var result = await RunPackageAsync(Package(("document.xml", "<DOC/>")));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.PackageDoesNotIdentifyFormex, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    private static async Task<EuFormexAnnexInventoryProductionResult> RunPackageAsync(byte[] bytes)
    {
        var (store, receipt, profile) = await FixtureAsync(bytes);
        return await new EuFormexAnnexInventoryProducer(store).RunAsync(
            receipt, profile.Bytes, profile.Reference, CancellationToken.None);
    }

    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        DurableBlobWriteReceipt Receipt, (byte[] Bytes, SourceArtifactRef Reference) Profile)>
        FixtureAsync(byte[] bytes)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        return (store, receipt, Profile(receipt.Reference.ContentSha256));
    }

    private static (byte[] Bytes, SourceArtifactRef Reference) Profile(string transportSha256)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-formex-annex-inventory-profile/1",
            "transport_sha256=" + transportSha256,
            "annex_root=ANNEX") + "\n");
        return (bytes, new SourceArtifactRef(
            "urn:uuid:88888888-8888-4888-8888-888888888888",
            Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }

    private static async Task<byte[]> FixtureBytesAsync(string name) => await File.ReadAllBytesAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name));

    private static byte[] Package(params (string Name, string Xml)[] entries)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }

        return bytes.ToArray();
    }

    private static string AnnexXml(
        string documentFile,
        string sequence,
        int pageFirst,
        int pageLast,
        int pageTotal,
        string title = "ANNEX") => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ANNEX xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xsi:noNamespaceSchemaLocation="http://formex.publications.europa.eu/schema/formex-test.xd">
          <BIB.INSTANCE>
            <DOCUMENT.REF FILE="{{documentFile}}"><NO.DOC><COM>EU</COM><YEAR>2026</YEAR><NO.CURRENT>1965</NO.CURRENT></NO.DOC></DOCUMENT.REF>
            <NO.SEQ>{{sequence}}</NO.SEQ>
            <PAGE.FIRST>{{pageFirst}}</PAGE.FIRST>
            <PAGE.LAST>{{pageLast}}</PAGE.LAST>
            <PAGE.TOTAL>{{pageTotal}}</PAGE.TOTAL>
          </BIB.INSTANCE>
          <TITLE><TI><P>{{title}}</P></TI></TITLE>
        </ANNEX>
        """;

    private static string DocumentXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <DOC xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             xsi:noNamespaceSchemaLocation="http://formex.publications.europa.eu/schema/formex-test.xd">
          <BIB.INSTANCE><NO.DOC>LEU20261965EN1101</NO.DOC></BIB.INSTANCE>
        </DOC>
        """;

}
