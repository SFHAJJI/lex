using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuImageOnlyAnnexProducerTests
{
    private const string CellarKey = "01234567-89ab-cdef-0123-456789abcdef";
    private const string AnnexAuthority = "http://publications.europa.eu/resource/authority/fd_370";

    [TestMethod]
    public async Task RetainedImageOnlyAnnexProducesTheTypedGap()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var pdf = MinimalPdf(image: true, text: false);
        var fixture = await FixtureAsync(store, pdf);
        var profile = Profile(fixture, 1);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            profile.Bytes,
            profile.Reference,
            CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsNotNull(result.Disposition);
        Assert.AreEqual(EuAnnexBodyDispositionOutcome.TextNotAvailable, result.Disposition.Outcome);
        Assert.AreEqual("image_only", result.Disposition.ReasonCode);
        Assert.AreEqual(fixture.Receipt.Reference.ContentSha256, result.Disposition.TransportByteSha256);
        Assert.AreEqual(profile.Reference, result.Disposition.ProfileRef);
    }

    [TestMethod]
    public async Task RepeatingTheSameEvidenceProducesTheSameDispositionIdentity()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var profile = Profile(fixture, 1);
        var producer = new EuImageOnlyAnnexProducer(store);

        var first = await RunAsync(producer, fixture, profile);
        var second = await RunAsync(producer, fixture, profile);

        Assert.IsNotNull(first.Disposition);
        Assert.IsNotNull(second.Disposition);
        Assert.AreEqual(first.Disposition.IdentitySha256, second.Disposition.IdentitySha256);
    }

    [TestMethod]
    public async Task TheCommittedOfficialPdfAnnexIsNotRelabelledImageOnly()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures", "EuDocumentFetch", "new-pdfa2a-200-body.bin");
        var pdf = await File.ReadAllBytesAsync(path);
        Assert.AreEqual(
            "72f2fc7053b7532f4515e00c365c3f996e54fab075ddb47a9cd1bd151cd4255f",
            Sha256(pdf));
        var fixture = await FixtureAsync(store, pdf);
        var profile = Profile(fixture, 2);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.AnnexContainsText, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AnImageFreePageIsNotCalledImageOnly()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: false, text: false));
        var profile = Profile(fixture, 1);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.AnnexContainsNoImage, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task ProfileBytesBindTheAnnexLocationAndSelectedPages()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var otherLocation = EuStructuralLocation.Parse(
            "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} IV",
            AnnexAuthority);
        var wrongAnnex = Profile(otherLocation, fixture.Receipt.Reference.ContentSha256, 1);
        var outOfRange = Profile(fixture, 2);

        var wrongAnnexResult = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            wrongAnnex.Bytes, wrongAnnex.Reference, CancellationToken.None);
        var outOfRangeResult = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            outOfRange.Bytes, outOfRange.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.ProfileDoesNotNameAnnex, wrongAnnexResult.Refusal);
        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.ProfilePageOutsideDocument, outOfRangeResult.Refusal);
    }

    [TestMethod]
    public async Task AProfileReferenceForOtherBytesIsRejectedBeforePdfInspection()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var profile = Profile(fixture, 1);
        var wrongReference = new SourceArtifactRef(profile.Reference.ResourceId, new string('a', 64));

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, wrongReference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.ProfileDigestMismatch, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task MissingCustodyBytesCannotProduceADisposition()
    {
        var realStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(realStore, MinimalPdf(image: true, text: false));
        var profile = Profile(fixture, 1);

        var result = await new EuImageOnlyAnnexProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.RetainedBytesUnavailable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task SubstitutedCustodyBytesCannotProduceADisposition()
    {
        var realStore = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var pdf = MinimalPdf(image: true, text: false);
        var fixture = await FixtureAsync(realStore, pdf);
        var profile = Profile(fixture, 1);
        var substituted = pdf.ToArray();
        substituted[^1] ^= 1;
        var store = ReadSubstitutingStore.Create(substituted);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.RetainedBytesUnavailable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task MalformedBytesCannotBeClassifiedAsAnImageOnlyPdf()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, "%PDF-not-a-document"u8.ToArray());
        var profile = Profile(fixture, 1);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AParserRecoverablePdfWithoutItsEndMarkerIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var complete = MinimalPdf(image: true, text: false);
        var truncated = complete[..^"%%EOF\n"u8.Length];
        var fixture = await FixtureAsync(store, truncated);
        var profile = Profile(fixture, 1);

        var result = await RunAsync(
            new EuImageOnlyAnnexProducer(store), fixture, profile);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AParserRecoverablePdfWithABrokenCrossReferenceIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var complete = Encoding.ASCII.GetString(MinimalPdf(image: true, text: false));
        var startXref = complete.LastIndexOf("startxref\n", StringComparison.Ordinal);
        var valueStart = startXref + "startxref\n".Length;
        var valueEnd = complete.IndexOf('\n', valueStart);
        var malformed = Encoding.ASCII.GetBytes(
            string.Concat(complete.AsSpan(0, valueStart), "1", complete.AsSpan(valueEnd)));
        var fixture = await FixtureAsync(store, malformed);
        var profile = Profile(fixture, 1);

        var result = await RunAsync(
            new EuImageOnlyAnnexProducer(store), fixture, profile);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AParserRecoverablePdfWithCorruptCrossReferenceEntriesIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var complete = Encoding.ASCII.GetString(MinimalPdf(image: true, text: false));
        var malformed = System.Text.RegularExpressions.Regex.Replace(
            complete,
            @"(?m)^\d{10} 00000 n $",
            "0000000001 00000 n ");
        Assert.AreNotEqual(complete, malformed);
        var fixture = await FixtureAsync(store, Encoding.ASCII.GetBytes(malformed));
        var profile = Profile(fixture, 1);

        var result = await RunAsync(
            new EuImageOnlyAnnexProducer(store), fixture, profile);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AParserRecoverablePdfWithInvalidObjectZeroIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var complete = Encoding.ASCII.GetString(MinimalPdf(image: true, text: false));
        var malformed = complete.Replace(
            "0000000000 65535 f ",
            "0000000000 00000 f ",
            StringComparison.Ordinal);
        Assert.AreNotEqual(complete, malformed);
        var fixture = await FixtureAsync(store, Encoding.ASCII.GetBytes(malformed));
        var profile = Profile(fixture, 1);

        var result = await RunAsync(
            new EuImageOnlyAnnexProducer(store), fixture, profile);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task AParserRecoverableFakeCrossReferenceStreamIsRefused()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var complete = Encoding.ASCII.GetString(MinimalPdf(image: true, text: false));
        var startXref = complete.LastIndexOf("startxref\n", StringComparison.Ordinal);
        var prefix = complete[..startXref];
        var fakeXrefOffset = Encoding.ASCII.GetByteCount(prefix);
        var malformed = Encoding.ASCII.GetBytes(prefix +
            "6 0 obj\n<< /Type /XRef >>\nendobj\nstartxref\n" +
            fakeXrefOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\n%%EOF\n");
        var fixture = await FixtureAsync(store, malformed);
        var profile = Profile(fixture, 1);

        var result = await RunAsync(
            new EuImageOnlyAnnexProducer(store), fixture, profile);

        Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task APageSelectionCannotBeReusedForDifferentPdfBytes()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var first = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var second = await FixtureAsync(store, MinimalPdf(image: false, text: false));
        var firstProfile = Profile(first, 1);

        var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
            second.SourceObject, second.Location, second.Address,
            second.Request, second.Request, second.Evidence, second.Receipt,
            firstProfile.Bytes, firstProfile.Reference, CancellationToken.None);

        Assert.AreEqual(
            EuImageOnlyAnnexProductionRefusal.ProfileDoesNotNameTransport,
            result.Refusal);
        Assert.IsNull(result.Disposition);
    }

    [TestMethod]
    public async Task EverySelectedPageMustBeImageOnly()
    {
        foreach (var (pdf, expected) in new[]
        {
            (MultiPagePdf((true, false), (true, true)),
                EuImageOnlyAnnexProductionRefusal.AnnexContainsText),
            (MultiPagePdf((true, false), (false, false)),
                EuImageOnlyAnnexProductionRefusal.AnnexContainsNoImage),
        })
        {
            var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var fixture = await FixtureAsync(store, pdf);
            var profile = Profile(fixture, 1, 2);

            var result = await new EuImageOnlyAnnexProducer(store).RunAsync(
                fixture.SourceObject, fixture.Location, fixture.Address,
                fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
                profile.Bytes, profile.Reference, CancellationToken.None);

            Assert.AreEqual(expected, result.Refusal, result.Detail);
            Assert.IsNull(result.Disposition);
        }
    }

    [TestMethod]
    public async Task NonCanonicalProfilesRefuseBeforeCustodyIsRead()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var canonical = Profile(fixture, 1).Bytes;
        var malformed = new[]
        {
            canonical[..^1],
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("pages=1", "pages=01")),
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("pages=1", "pages=1,1")),
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace(
                "classification=pdfpig-0.1.11:no_glyphs+image",
                "classification=pdfpig-0.1.11:image")),
            new byte[] { 0xff, 0xfe },
        };

        foreach (var bytes in malformed)
        {
            var profileRef = new SourceArtifactRef(
                "urn:uuid:77777777-7777-4777-8777-777777777777", Sha256(bytes));
            var result = await new EuImageOnlyAnnexProducer(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).RunAsync(
                fixture.SourceObject, fixture.Location, fixture.Address,
                fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
                bytes, profileRef, CancellationToken.None);

            Assert.AreEqual(EuImageOnlyAnnexProductionRefusal.ProfileInvalid, result.Refusal);
            Assert.IsNull(result.Disposition);
        }
    }

    [TestMethod]
    public async Task CancellationStopsBeforeCustodyOrPdfWork()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var fixture = await FixtureAsync(store, MinimalPdf(image: true, text: false));
        var profile = Profile(fixture, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new EuImageOnlyAnnexProducer(
                new EuAcquisitionTestFixture.EuInMemoryCustodyStore()).RunAsync(
                fixture.SourceObject, fixture.Location, fixture.Address,
                fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
                profile.Bytes, profile.Reference, cancellation.Token));
    }

    private static async Task<FixtureValues> FixtureAsync(ICustodyStore store, byte[] pdf)
    {
        var receipt = await store.CreateAsync(pdf, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var sourceObject = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(ArtifactRef('1', '2'), "manifestation"),
            "http://publications.europa.eu/resource/cellar/" + CellarKey,
            CellarKey,
            Sha256(Encoding.UTF8.GetBytes(CellarKey)),
            ArtifactRef('3', '4'),
            parentKeyRef: null);
        var location = EuStructuralLocation.Parse(
            "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} III",
            AnnexAuthority);
        var address = EuDocumentFetchAddress.TryCreate(
            "cellar", CellarKey, EuManifestationMediaType.ApplicationPdf,
            EuDocumentLanguage.Eng, out var refusal)!;
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);
        var request = HttpLogicalRequest.Create(
            address.ResourceUri,
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("accept", address.Accept),
                new HttpLogicalRequestHeader("accept-language", address.AcceptLanguage),
            ],
            new HttpLogicalRequestBody(0, Sha256([])),
            new string('1', 64),
            new string('2', 64));
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            Sha256(request.CopyCanonicalBytes()),
            address.ResourceUri,
            200,
            Headers(address.Accept, checked((ulong)pdf.Length)),
            "2026-09-13T12:00:00.0000000Z",
            "2026-09-13T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(checked((ulong)pdf.Length)),
            checked((ulong)pdf.Length),
            receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt),
            checked((ulong)pdf.Length),
            receipt.Reference.ContentSha256);
        var evidence = RoutedHttpEvidence.Create(
            ArtifactRef('5', '6'),
            1,
            0,
            [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
        return new FixtureValues(sourceObject, location, address, request, evidence, receipt);
    }

    private static Task<EuImageOnlyAnnexProductionResult> RunAsync(
        EuImageOnlyAnnexProducer producer,
        FixtureValues fixture,
        (byte[] Bytes, SourceArtifactRef Reference) profile) => producer.RunAsync(
            fixture.SourceObject, fixture.Location, fixture.Address,
            fixture.Request, fixture.Request, fixture.Evidence, fixture.Receipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

    private static (byte[] Bytes, SourceArtifactRef Reference) Profile(
        FixtureValues fixture,
        params int[] pages) => Profile(
            fixture.Location, fixture.Receipt.Reference.ContentSha256, pages);

    private static (byte[] Bytes, SourceArtifactRef Reference) Profile(
        EuStructuralLocation location,
        string transportSha256,
        params int[] pages)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-image-only-annex-profile/1",
            "annex_location_sha256=" + Sha256(Encoding.UTF8.GetBytes(location.RawValue)),
            "transport_sha256=" + transportSha256,
            "pages=" + string.Join(',', pages),
            "classification=pdfpig-0.1.11:no_glyphs+image") + "\n");
        return (bytes, new SourceArtifactRef(
            "urn:uuid:77777777-7777-4777-8777-777777777777", Sha256(bytes)));
    }

    private static byte[] MinimalPdf(bool image, bool text)
        => MultiPagePdf((image, text));

    private static byte[] MultiPagePdf(params (bool Image, bool Text)[] pages)
    {
        if (pages.Length == 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(pages));
        }

        var pageObjectStart = 3;
        var nextObject = pageObjectStart + pages.Length;
        var imageObject = pages.Any(static page => page.Image) ? nextObject++ : 0;
        var fontObject = pages.Any(static page => page.Text) ? nextObject++ : 0;
        var contentObjectStart = nextObject;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(pageObjectStart, pages.Length).Select(static number => $"{number} 0 R"))}] /Count {pages.Length} >>",
        };

        for (var index = 0; index < pages.Length; index++)
        {
            var resources = new List<string>();
            if (pages[index].Image)
            {
                resources.Add($"/XObject << /Im0 {imageObject} 0 R >>");
            }
            if (pages[index].Text)
            {
                resources.Add($"/Font << /F1 {fontObject} 0 R >>");
            }
            var resourceDictionary = resources.Count == 0
                ? string.Empty
                : " /Resources << " + string.Join(' ', resources) + " >>";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100]{resourceDictionary} /Contents {contentObjectStart + index} 0 R >>");
        }

        if (imageObject != 0)
        {
            objects.Add("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\nX\nendstream");
        }
        if (fontObject != 0)
        {
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        }

        foreach (var page in pages)
        {
            var content = page.Image ? "q 100 0 0 100 0 0 cm /Im0 Do Q" : string.Empty;
            if (page.Text)
            {
                content += " BT /F1 12 Tf 10 50 Td (invented) Tj ET";
            }
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }
        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static RoutedHttpResponseHeaders Headers(string mediaType, ulong length)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader(mediaType),
            new RoutedHttpSingleHeader(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
    }

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record FixtureValues(
        SourceObjectRef SourceObject,
        EuStructuralLocation Location,
        EuDocumentFetchAddress Address,
        HttpLogicalRequest Request,
        RoutedHttpEvidence Evidence,
        DurableBlobWriteReceipt Receipt);

    private class ReadSubstitutingStore : DispatchProxy
    {
        private byte[] _bytes = [];

        internal static ICustodyStore Create(byte[] bytes)
        {
            var proxy = Create<ICustodyStore, ReadSubstitutingStore>();
            ((ReadSubstitutingStore)(object)proxy)._bytes = bytes;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICustodyStore.ReadAsync))
            {
                return Task.FromResult<ReadOnlyMemory<byte>>(_bytes);
            }

            throw new InvalidOperationException($"Unexpected custody call: {targetMethod?.Name}");
        }
    }

}
