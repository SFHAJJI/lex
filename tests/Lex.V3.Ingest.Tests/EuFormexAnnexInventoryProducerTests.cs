using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuFormexAnnexInventoryProducerTests
{
    [TestMethod]
    public async Task RetainedFormexPackageDerivesItsAnnexPopulation()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, binding, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
            binding, profile.Bytes, profile.Reference, CancellationToken.None);

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
        var (store, _, binding, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
            binding, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Inventory);
        Assert.AreEqual(0, result.Inventory.Members.Count);
    }

    [TestMethod]
    public async Task RepeatingTheSameEvidenceProducesTheSameInventoryIdentity()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, _, binding, profile) = await FixtureAsync(bytes);
        var producer = new EuFormexAnnexInventoryProducer(store);

        var first = await producer.RunAsync(binding, profile.Bytes, profile.Reference, CancellationToken.None);
        var second = await producer.RunAsync(binding, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.IsNotNull(first.Inventory);
        Assert.IsNotNull(second.Inventory);
        Assert.AreEqual(first.Inventory.IdentitySha256, second.Inventory.IdentitySha256);
    }

    [TestMethod]
    public async Task StableRuleProfileRejectsChangedRulesOrDigest()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, _, binding, profile) = await FixtureAsync(bytes);
        var wrongProfileBytes = profile.Bytes.ToArray();
        wrongProfileBytes[wrongProfileBytes.AsSpan().IndexOf("ANNEX"u8)] = (byte)'X';
        var wrongProfile = (Bytes: wrongProfileBytes,
            Reference: new SourceArtifactRef(profile.Reference.ResourceId, Sha(wrongProfileBytes)));
        var wrongReference = new SourceArtifactRef(profile.Reference.ResourceId, new string('b', 64));
        var producer = new EuFormexAnnexInventoryProducer(store);

        var wrongRule = await producer.RunAsync(
            binding, wrongProfile.Bytes, wrongProfile.Reference, CancellationToken.None);
        var wrongDigest = await producer.RunAsync(
            binding, profile.Bytes, wrongReference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.ProfileInvalid, wrongRule.Refusal);
        Assert.AreEqual(EuFormexAnnexInventoryRefusal.ProfileDigestMismatch, wrongDigest.Refusal);
    }

    [TestMethod]
    public async Task EqualCanonicalContentHasOneIdentityAcrossDifferentZipTransports()
    {
        var entries = new[]
        {
            ("document.xml", DocumentXml()),
            ("annex.xml", AnnexXml("document.xml", "0001.0001", 2, 7, 6)),
        };
        var firstBytes = Package([.. entries, ("transport-one.bin", "one")]);
        var secondBytes = Package([("transport-two.bin", "two"), .. entries.Reverse()]);
        var firstFixture = await FixtureAsync(firstBytes);
        var secondFixture = await FixtureAsync(secondBytes);
        var first = await new EuFormexAnnexInventoryProducer(firstFixture.Store).RunAsync(
            firstFixture.Binding, firstFixture.Profile.Bytes, firstFixture.Profile.Reference,
            CancellationToken.None);
        var second = await new EuFormexAnnexInventoryProducer(secondFixture.Store).RunAsync(
            secondFixture.Binding, secondFixture.Profile.Bytes, secondFixture.Profile.Reference,
            CancellationToken.None);

        Assert.IsNotNull(first.Inventory);
        Assert.IsNotNull(second.Inventory);
        Assert.AreNotEqual(
            first.Inventory.SourceReceipt.Reference.ContentSha256,
            second.Inventory.SourceReceipt.Reference.ContentSha256);
        Assert.AreEqual(first.Inventory.IdentitySha256, second.Inventory.IdentitySha256);
        Assert.AreNotEqual(
            Sha(first.Inventory.TransportBinding.ResponseEvidence.CopyCanonicalBytes()),
            Sha(second.Inventory.TransportBinding.ResponseEvidence.CopyCanonicalBytes()));
    }

    [TestMethod]
    public async Task SemanticIdentityUsesExpressionAndRuleDigestOnlyFromTheBindingAndProfile()
    {
        var bytes = Package(
            ("document.xml", DocumentXml()),
            ("annex.xml", AnnexXml("document.xml", "0001.0001", 2, 7, 6)));
        var fixture = await FixtureAsync(bytes);
        var produced = await new EuFormexAnnexInventoryProducer(fixture.Store).RunAsync(
            fixture.Binding, fixture.Profile.Bytes, fixture.Profile.Reference,
            CancellationToken.None);
        var inventory = produced.Inventory!;
        var sameDigestDifferentResource = new EuFormexAnnexInventory(
            fixture.Binding,
            Artifact('9', fixture.Profile.Reference.Sha256),
            inventory.Members);
        var differentRuleDigest = new EuFormexAnnexInventory(
            fixture.Binding,
            Artifact('8', new string('8', 64)),
            inventory.Members);

        var otherPackage = FormexPackage(".0002");
        var otherRequest = Request(otherPackage.BodyRef);
        var otherBinding = new EuFormexAnnexTransportBinding(
            otherPackage,
            otherRequest,
            Response(otherRequest, fixture.Receipt),
            fixture.Receipt);
        var otherExpressionInventory = new EuFormexAnnexInventory(
            otherBinding,
            fixture.Profile.Reference,
            inventory.Members);

        Assert.AreEqual(inventory.IdentitySha256, sameDigestDifferentResource.IdentitySha256);
        Assert.AreNotEqual(inventory.IdentitySha256, differentRuleDigest.IdentitySha256);
        Assert.AreNotEqual(inventory.IdentitySha256, otherExpressionInventory.IdentitySha256);
    }

    [TestMethod]
    public async Task TransportBindingRejectsPackageBodyRequestSubstitution()
    {
        var bytes = Package(("document.xml", DocumentXml()));
        var fixture = await FixtureAsync(bytes);
        var package = FormexPackage();
        var wrongRequest = HttpLogicalRequest.Create(
            "https://publications.europa.eu/resource/cellar/other-formex",
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("accept", "application/zip;mtype=fmx4")],
            new HttpLogicalRequestBody(0, Sha([])),
            new string('3', 64),
            new string('4', 64));
        var wrongAccept = HttpLogicalRequest.Create(
            "https://publications.europa.eu/resource/cellar/" + package.BodyRef.CanonicalKey,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("accept", "application/zip")],
            new HttpLogicalRequestBody(0, Sha([])),
            new string('3', 64),
            new string('4', 64));

        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package,
            wrongRequest,
            fixture.Binding.ResponseEvidence,
            fixture.Receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package,
            wrongAccept,
            fixture.Binding.ResponseEvidence,
            fixture.Receipt));
    }

    [TestMethod]
    public async Task TransportBindingRejectsEachResponseAndReceiptSubstitution()
    {
        var bytes = Package(("document.xml", DocumentXml()));
        var fixture = await FixtureAsync(bytes);
        var package = FormexPackage();
        var request = fixture.Binding.RequestEvidence;
        var wrongRequestDigest = Response(
            request, fixture.Receipt, logicalRequestSha256: new string('6', 64));
        var wrongRequestUri = Response(
            request, fixture.Receipt,
            requestUri: "https://publications.europa.eu/resource/cellar/other-formex");
        var nonSuccessResponse = Response(request, fixture.Receipt, status: 404);
        var incompleteResponse = Response(
            request,
            fixture.Receipt,
            outcome: new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.SourceProfileStale));
        var policy = fixture.Receipt.PolicyEvidence;
        var substitutedReceipt = new DurableBlobWriteReceipt(
            fixture.Receipt.Schema,
            fixture.Receipt.Reference,
            new CustodyPolicyEvidence(
                policy.Schema,
                policy.Reference,
                policy.VerificationProfile,
                policy.PolicyKey,
                policy.Protection,
                policy.ObservedAt.AddSeconds(1),
                policy.ProtectedUntil));

        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package, request, wrongRequestDigest, fixture.Receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package, request, wrongRequestUri, fixture.Receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package, request, nonSuccessResponse, fixture.Receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package, request, incompleteResponse, fixture.Receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new EuFormexAnnexTransportBinding(
            package, request, fixture.Binding.ResponseEvidence, substitutedReceipt));
    }

    [TestMethod]
    public async Task MissingCustodyBytesCannotProduceAnInventory()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (_, _, binding, profile) = await FixtureAsync(bytes);

        var result = await new EuFormexAnnexInventoryProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).RunAsync(
                binding, profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.RetainedBytesUnavailable, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task SubstitutedCustodyBytesCannotProduceAnInventory()
    {
        var bytes = await FixtureBytesAsync("new-fmx4-200-body.bin");
        var (store, receipt, binding, profile) = await FixtureAsync(bytes);
        var substituted = bytes.ToArray();
        substituted[^1] ^= 1;
        var heldBytes = (Dictionary<string, byte[]>)typeof(EuAcquisitionTestFixture.EuInMemoryCustodyStore)
            .GetField("_byDigest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        heldBytes[receipt.Reference.ContentSha256] = substituted;

        var result = await new EuFormexAnnexInventoryProducer(store).RunAsync(
                binding, profile.Bytes, profile.Reference, CancellationToken.None);

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

    [TestMethod]
    public async Task AnAnnexNameWithoutAFormexSchemaCannotEnterTheInventory()
    {
        var result = await RunPackageAsync(Package(
            ("document.xml", DocumentXml()),
            ("annex.xml", """
                <ANNEX>
                  <BIB.INSTANCE>
                    <DOCUMENT.REF FILE="document.xml">LEU20261965EN1101</DOCUMENT.REF>
                    <NO.SEQ>0001.0001</NO.SEQ>
                    <PAGE.FIRST>2</PAGE.FIRST>
                    <PAGE.LAST>7</PAGE.LAST>
                    <PAGE.TOTAL>6</PAGE.TOTAL>
                  </BIB.INSTANCE>
                  <TITLE><TI><P>ANNEX</P></TI></TITLE>
                </ANNEX>
                """)));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.PackageDoesNotIdentifyFormex, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    [TestMethod]
    public async Task ADocumentTypeDeclarationIsRefused()
    {
        var documentWithDtd = DocumentXml().Replace(
            "?>",
            "?>\n<!DOCTYPE DOC [<!ELEMENT DOC ANY>]>",
            StringComparison.Ordinal);
        var result = await RunPackageAsync(Package(("document.xml", documentWithDtd)));

        Assert.AreEqual(EuFormexAnnexInventoryRefusal.XmlInvalid, result.Refusal);
        Assert.IsNull(result.Inventory);
    }

    private static async Task<EuFormexAnnexInventoryProductionResult> RunPackageAsync(byte[] bytes)
    {
        var (store, _, binding, profile) = await FixtureAsync(bytes);
        return await new EuFormexAnnexInventoryProducer(store).RunAsync(
            binding, profile.Bytes, profile.Reference, CancellationToken.None);
    }

    internal static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        DurableBlobWriteReceipt Receipt, EuFormexAnnexTransportBinding Binding,
        (byte[] Bytes, SourceArtifactRef Reference) Profile)>
        FixtureAsync(byte[] bytes)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var package = FormexPackage();
        var request = Request(package.BodyRef);
        var response = Response(request, receipt);
        return (store, receipt,
            new EuFormexAnnexTransportBinding(package, request, response, receipt),
            Profile());
    }

    private static (byte[] Bytes, SourceArtifactRef Reference) Profile()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-formex-annex-interpretation-profile/1",
            "document_root=DOC",
            "annex_root=ANNEX",
            "schema_prefix=http://formex.publications.europa.eu/schema/formex-",
            "member_identity=document_reference_file+sequence",
            "ordering=sequence+package_entry",
            "title=required",
            "page_extent=inclusive_positive_consistent") + "\n");
        return (bytes, new SourceArtifactRef(
            "urn:uuid:88888888-8888-4888-8888-888888888888",
            Sha(bytes)));
    }

    private static EuFormexPackage FormexPackage(
        string suffix = ".0001")
    {
        var registry = Artifact('1', new string('1', 64));
        var identityProfile = Artifact('2', new string('2', 64));
        var boundary = new EuWemiIdentityBoundary(registry, identityProfile);
        const string workKey = "5f2552c2-11bd-11e6-ba9a-01aa75ed71a1";
        var workKind = new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(EuWemiRole.Work));
        var work = new SourceObjectRef(SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar,
            workKind, "http://publications.europa.eu/resource/cellar/" + workKey,
            workKey, Sha(Encoding.UTF8.GetBytes(workKey)), identityProfile, null);
        var expressionKey = workKey + suffix;
        var expressionKind = new SourceRegistryMemberRef(
            registry, EuWemiIdentityBoundary.MemberKeyOf(EuWemiRole.Expression));
        var parent = new SourceObjectKeyRef(work.EntityKind, work.PublisherUri,
            work.CanonicalKey, work.CanonicalKeySha256);
        var expression = new SourceObjectRef(SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar,
            expressionKind, "http://publications.europa.eu/resource/cellar/" + expressionKey,
            expressionKey, Sha(Encoding.UTF8.GetBytes(expressionKey)), identityProfile, parent);
        var manifestationKey = expressionKey + ".01";
        var manifestation = Object(
            boundary, registry, identityProfile, manifestationKey,
            EuWemiRole.Manifestation, expression);
        var item = Object(
            boundary, registry, identityProfile, manifestationKey + "/FORMEX",
            EuWemiRole.Item, manifestation);
        var stream = EuFormexStreamName.TryParse(
            "CL2026R1965EN0000010.0001.xml", "32026R1965", out var roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var items = EuFormexItemSet.TryAdmit(
            [new EuFormexItem(boundary, stream, item, 0)], out roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var package = EuFormexPackage.TryAdmit(
            boundary, manifestation, expression, items, "EN", out var packageRefusal)!;
        Assert.AreEqual(EuFormexPackageRefusal.None, packageRefusal);
        return package;
    }

    private static SourceObjectRef Object(
        EuWemiIdentityBoundary boundary,
        SourceArtifactRef registry,
        SourceArtifactRef identityProfile,
        string key,
        EuWemiRole role,
        SourceObjectRef parent)
    {
        var kind = new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(role));
        var parentKey = new SourceObjectKeyRef(
            parent.EntityKind, parent.PublisherUri, parent.CanonicalKey, parent.CanonicalKeySha256);
        var value = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            kind,
            "http://publications.europa.eu/resource/cellar/" + key,
            key,
            Sha(Encoding.UTF8.GetBytes(key)),
            identityProfile,
            parentKey);
        return boundary.Require(value, role, nameof(value));
    }

    private static HttpLogicalRequest Request(SourceObjectRef body) => HttpLogicalRequest.Create(
        "https://publications.europa.eu/resource/cellar/" + body.CanonicalKey,
        HttpRequestMethod.Get,
        [new HttpLogicalRequestHeader("accept", "application/zip;mtype=fmx4")],
        new HttpLogicalRequestBody(0, Sha([])),
        new string('3', 64),
        new string('4', 64));

    private static RoutedHttpEvidence Response(
        HttpLogicalRequest request,
        DurableBlobWriteReceipt receipt,
        int status = 200,
        string? logicalRequestSha256 = null,
        string? requestUri = null,
        RoutedHttpRouteOutcome? outcome = null)
    {
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            logicalRequestSha256 ?? Sha(request.CopyCanonicalBytes()),
            requestUri ?? request.Uri,
            status,
            Headers(checked((ulong)receipt.Reference.ByteLength)),
            "2026-09-14T12:00:00.0000000Z",
            "2026-09-14T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(checked((ulong)receipt.Reference.ByteLength)),
            checked((ulong)receipt.Reference.ByteLength),
            receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt),
            checked((ulong)receipt.Reference.ByteLength),
            receipt.Reference.ContentSha256);
        return RoutedHttpEvidence.Create(
            Artifact('5', new string('5', 64)), 1, 0, [hop],
            outcome ?? new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
    }

    private static RoutedHttpResponseHeaders Headers(ulong length)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new(new RoutedHttpSingleHeader("application/zip;mtype=fmx4"),
            new RoutedHttpSingleHeader(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
    }

    private static SourceArtifactRef Artifact(char fill, string digest) => new(
        $"urn:uuid:{new string(fill, 8)}-{new string(fill, 4)}-4{new string(fill, 3)}-8{new string(fill, 3)}-{new string(fill, 12)}",
        digest);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

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
