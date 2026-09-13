using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Derivation;

[TestClass]
public sealed class EuAnnexBodyDispositionTests
{
    private const string CellarKey = "01234567-89ab-cdef-0123-456789abcdef";
    private const string AnnexAuthority = "http://publications.europa.eu/resource/authority/fd_370";
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [TestMethod]
    public void ImageOnlyGapRetainsOfficialIdentityAddressAndExactByteLineage()
    {
        var fixture = Fixture();

        var disposition = EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable);

        Assert.AreEqual(EuAnnexBodyDispositionOutcome.TextNotAvailable, disposition.Outcome);
        Assert.AreEqual("image_only", disposition.ReasonCode);
        Assert.AreSame(fixture.SourceObject, disposition.SourceObject);
        Assert.AreSame(fixture.Location, disposition.AnnexLocation);
        Assert.AreSame(fixture.Address, disposition.OfficialAddress);
        Assert.AreEqual(fixture.Address.ResourceUri, disposition.SourceObservation.RequestedUri);
        Assert.AreSame(fixture.Receipt, disposition.RetainedTransportBytes);
        Assert.AreSame(fixture.ProfileRef, disposition.ProfileRef);
        Assert.AreEqual(fixture.Receipt.Reference.ContentSha256, disposition.TransportByteSha256);
    }

    [TestMethod]
    public void OutcomesAreClosedToAdmittedRejectedAndTheTypedGap()
    {
        CollectionAssert.AreEqual(
            new[] { "Admitted", "Rejected", "TextNotAvailable" },
            Enum.GetNames<EuAnnexBodyDispositionOutcome>());

        var fixture = Fixture();
        Assert.AreEqual(
            "body_admitted",
            Create(fixture, EuAnnexBodyDispositionOutcome.Admitted).ReasonCode);
        Assert.AreEqual(
            "profile_rejected",
            Create(fixture, EuAnnexBodyDispositionOutcome.Rejected).ReasonCode);
    }

    [TestMethod]
    public void TheContractHasNoDoorForOcrOrReconstructedWording()
    {
        var surface = typeof(EuAnnexBodyDisposition)
            .GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(member =>
                $"{member.MemberType} {(IsStaticMember(member) ? "static" : "instance")} {member}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Method instance Boolean Equals(Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition)",
                "Method instance Boolean Equals(System.Object)",
                "Method instance Int32 GetHashCode()",
                "Method instance Lex.V3.Contracts.Custody.DurableBlobWriteReceipt "
                    + "get_RetainedTransportBytes()",
                "Method instance Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition <Clone>$()",
                "Method instance Lex.V3.Contracts.Derivation.EuAnnexBodyDispositionOutcome get_Outcome()",
                "Method instance Lex.V3.Contracts.Source.Core.SourceArtifactRef get_ProfileRef()",
                "Method instance Lex.V3.Contracts.Source.Core.SourceObjectRef get_SourceObject()",
                "Method instance Lex.V3.Contracts.Source.Europe.EuDocumentFetchAddress "
                    + "get_OfficialAddress()",
                "Method instance Lex.V3.Contracts.Source.Europe.EuStructuralLocation get_AnnexLocation()",
                "Method instance Lex.V3.Contracts.Source.Http.RepresentationChainObservation "
                    + "get_SourceObservation()",
                "Method instance System.String ToString()",
                "Method instance System.String get_IdentitySha256()",
                "Method instance System.String get_ReasonCode()",
                "Method instance System.String get_TransportByteSha256()",
                "Method static Boolean op_Equality(Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition, "
                    + "Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition)",
                "Method static Boolean op_Inequality(Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition, "
                    + "Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition)",
                "Method static Lex.V3.Contracts.Derivation.EuAnnexBodyDisposition Create("
                    + "Lex.V3.Contracts.Source.Core.SourceObjectRef, "
                    + "Lex.V3.Contracts.Source.Europe.EuStructuralLocation, "
                    + "Lex.V3.Contracts.Source.Europe.EuDocumentFetchAddress, "
                    + "Lex.V3.Contracts.Source.Http.HttpLogicalRequest, "
                    + "Lex.V3.Contracts.Source.Http.HttpLogicalRequest, "
                    + "Lex.V3.Contracts.Source.Http.RoutedHttpEvidence, "
                    + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                    + "Lex.V3.Contracts.Derivation.EuAnnexBodyDispositionOutcome)",
                "Property instance Lex.V3.Contracts.Custody.DurableBlobWriteReceipt "
                    + "RetainedTransportBytes",
                "Property instance Lex.V3.Contracts.Derivation.EuAnnexBodyDispositionOutcome Outcome",
                "Property instance Lex.V3.Contracts.Source.Core.SourceArtifactRef ProfileRef",
                "Property instance Lex.V3.Contracts.Source.Core.SourceObjectRef SourceObject",
                "Property instance Lex.V3.Contracts.Source.Europe.EuDocumentFetchAddress OfficialAddress",
                "Property instance Lex.V3.Contracts.Source.Europe.EuStructuralLocation AnnexLocation",
                "Property instance Lex.V3.Contracts.Source.Http.RepresentationChainObservation "
                    + "SourceObservation",
                "Property instance System.String IdentitySha256",
                "Property instance System.String ReasonCode",
                "Property instance System.String TransportByteSha256",
            },
            surface,
            "the disposition's declared public surface changed; it may retain evidence and a typed "
            + "outcome, but no OCR output or reconstructed wording. Observed: "
            + string.Join(" | ", surface));
    }

    [TestMethod]
    public void AnAddressTheObservedRouteDidNotStartAtIsRejected()
    {
        var fixture = Fixture();
        var other = EuDocumentFetchAddress.TryCreate(
            "cellar",
            CellarKey,
            EuManifestationMediaType.PdfTypePdfa2a,
            EuDocumentLanguage.Eng,
            out var refusal);
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            other!,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void AnOfficialRedirectRetainsItsStartAndEffectiveAddresses()
    {
        var fixture = Fixture();
        var terminalUri = fixture.Address.ResourceUri + "?download=1";
        var terminalRequest = HttpLogicalRequest.Create(
            terminalUri,
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("accept", fixture.Address.Accept),
                new HttpLogicalRequestHeader("accept-language", fixture.Address.AcceptLanguage),
            ],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));
        var officialRequest = fixture.Request;
        var requestDigest = Sha256(terminalRequest.CopyCanonicalBytes());
        var firstReceipt = Receipt(EmptyDigest, 0);
        const string firstHopId = "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        var first = RoutedHttpHop.Create(
            0,
            firstHopId,
            null,
            Sha256(officialRequest.CopyCanonicalBytes()),
            fixture.Address.ResourceUri,
            301,
            RedirectHeaders(terminalUri),
            "2026-09-12T20:00:00.0000000Z",
            "2026-09-12T20:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(0),
            0,
            EmptyDigest,
            DurableBlobWriteReceiptDigest.Of(firstReceipt),
            0,
            EmptyDigest);
        var terminal = RoutedHttpHop.Create(
            1,
            "urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc",
            firstHopId,
            requestDigest,
            terminalUri,
            200,
            Headers(),
            "2026-09-12T20:00:02.0000000Z",
            "2026-09-12T20:00:03.0000000Z",
            new DeclaredContentLengthHttpCompletion(3),
            3,
            Digest('a'),
            DurableBlobWriteReceiptDigest.Of(fixture.Receipt),
            3,
            Digest('a'));
        var evidence = RoutedHttpEvidence.Create(
            ArtifactRef('5', '6'),
            1,
            0,
            [first, terminal],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt>
            {
                [first.ObservationId] = firstReceipt,
                [terminal.ObservationId] = fixture.Receipt,
            });

        var disposition = EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            officialRequest,
            terminalRequest,
            evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable);

        Assert.AreEqual(fixture.Address.ResourceUri, disposition.SourceObservation.RequestedUri);
        Assert.AreEqual(terminalUri, disposition.SourceObservation.EffectiveUri);
    }

    [TestMethod]
    public void ARedirectCannotSubstituteTheOfficialFirstHopRequest()
    {
        var fixture = Fixture();
        var terminalUri = fixture.Address.ResourceUri + "?download=1";
        var substitutedFirstRequest = HttpLogicalRequest.Create(
            fixture.Address.ResourceUri,
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("accept", fixture.Address.Accept),
                new HttpLogicalRequestHeader("accept-language", fixture.Address.AcceptLanguage),
                new HttpLogicalRequestHeader("x-extra", "not-part-of-the-address"),
            ],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));
        var terminalRequest = HttpLogicalRequest.Create(
            terminalUri,
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("accept", fixture.Address.Accept),
                new HttpLogicalRequestHeader("accept-language", fixture.Address.AcceptLanguage),
            ],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));
        var firstReceipt = Receipt(EmptyDigest, 0);
        const string firstHopId = "urn:uuid:dddddddd-dddd-4ddd-8ddd-dddddddddddd";
        var first = RoutedHttpHop.Create(
            0,
            firstHopId,
            null,
            Sha256(substitutedFirstRequest.CopyCanonicalBytes()),
            fixture.Address.ResourceUri,
            301,
            RedirectHeaders(terminalUri),
            "2026-09-12T20:00:00.0000000Z",
            "2026-09-12T20:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(0),
            0,
            EmptyDigest,
            DurableBlobWriteReceiptDigest.Of(firstReceipt),
            0,
            EmptyDigest);
        var terminal = RoutedHttpHop.Create(
            1,
            "urn:uuid:eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee",
            firstHopId,
            Sha256(terminalRequest.CopyCanonicalBytes()),
            terminalUri,
            200,
            Headers(),
            "2026-09-12T20:00:02.0000000Z",
            "2026-09-12T20:00:03.0000000Z",
            new DeclaredContentLengthHttpCompletion(3),
            3,
            Digest('a'),
            DurableBlobWriteReceiptDigest.Of(fixture.Receipt),
            3,
            Digest('a'));
        var evidence = RoutedHttpEvidence.Create(
            ArtifactRef('5', '6'),
            1,
            0,
            [first, terminal],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt>
            {
                [first.ObservationId] = firstReceipt,
                [terminal.ObservationId] = fixture.Receipt,
            });

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            terminalRequest,
            evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void AnAddressWithAnUnrequestedLanguageHeaderIsRejected()
    {
        var fixture = Fixture();
        var other = EuDocumentFetchAddress.TryCreate(
            "cellar",
            CellarKey,
            EuManifestationMediaType.ApplicationPdf,
            EuDocumentLanguage.Fra,
            out var refusal);
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            other!,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void AnExtraRequestHeaderCannotBeFoldedIntoTheOfficialAddress()
    {
        var fixture = Fixture(includeExtraRequestHeader: true);

        Assert.ThrowsExactly<ArgumentException>(() => Create(
            fixture,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void ACallerClaimingCellarForAnUnrelatedPublisherUriIsRejected()
    {
        var fixture = Fixture(
            sourcePublisherUri: "https://example.invalid/resource/cellar/" + CellarKey);

        Assert.ThrowsExactly<ArgumentException>(() => Create(
            fixture,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void AReceiptForDifferentBytesIsRejected()
    {
        var fixture = Fixture();

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            Receipt(Digest('b'), 3),
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void ASubstitutedReceiptForTheSameBytesIsRejected()
    {
        var fixture = Fixture();

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            Receipt(Digest('a'), 3, "2026-09-12T20:00:02Z"),
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void ATextFormatCannotBeCalledAnImageOnlyPdfGap()
    {
        var fixture = Fixture(EuManifestationMediaType.XhtmlXml);

        Assert.ThrowsExactly<ArgumentException>(() => Create(
            fixture,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void ANonPdfResponseCannotBeCalledImageOnlyWhenPdfWasRequested()
    {
        var fixture = Fixture(responseMediaType: "text/html");

        Assert.ThrowsExactly<ArgumentException>(() => Create(
            fixture,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void ALocationWithoutAnAnnexTokenIsRejected()
    {
        var fixture = Fixture();
        var article = EuStructuralLocation.Parse(
            "{AR|http://publications.europa.eu/resource/authority/fd_370/AR} 1",
            AnnexAuthority);

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            article,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void AnAnnexCodeFromAnUnrelatedAuthorityListIsRejected()
    {
        var fixture = Fixture();
        const string foreignAuthority = "https://example.invalid/authority/fd_370";
        var foreignAnnex = EuStructuralLocation.Parse(
            "{AN|" + foreignAuthority + "/AN} III",
            foreignAuthority);

        Assert.ThrowsExactly<ArgumentException>(() => EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            foreignAnnex,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable));
    }

    [TestMethod]
    public void RepeatingTheDispositionOverTheSameEvidenceIsStable()
    {
        var fixture = Fixture();
        var first = Create(fixture, EuAnnexBodyDispositionOutcome.TextNotAvailable);
        var second = Create(fixture, EuAnnexBodyDispositionOutcome.TextNotAvailable);

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(first.TransportByteSha256, second.TransportByteSha256);
    }

    [TestMethod]
    public void StableIdentityChangesWithEachSemanticInput()
    {
        var baselineFixture = Fixture();
        var baseline = Create(baselineFixture, EuAnnexBodyDispositionOutcome.TextNotAvailable);

        Assert.AreEqual(
            "1202090e93ab268aa1389adc001d66c94ef538819bda238078ad09ab419eadb4",
            baseline.IdentitySha256,
            "the golden identity pins every canonical component, including the structurally bound "
            + "source and address contributions, so deleting either cannot be masked by the other.");
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(baselineFixture, EuAnnexBodyDispositionOutcome.Rejected).IdentitySha256);
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(Fixture(annexValue: "IV"), EuAnnexBodyDispositionOutcome.TextNotAvailable).IdentitySha256);
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(Fixture(byteFill: 'b'), EuAnnexBodyDispositionOutcome.TextNotAvailable).IdentitySha256);
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(Fixture(profileFill: '9'), EuAnnexBodyDispositionOutcome.TextNotAvailable).IdentitySha256);
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(
                Fixture(cellarKey: "11234567-89ab-cdef-0123-456789abcdef"),
                EuAnnexBodyDispositionOutcome.TextNotAvailable)
                .IdentitySha256,
            "a second valid source identity and its necessarily matching official address must move "
            + "the disposition identity.");
        Assert.AreNotEqual(
            baseline.IdentitySha256,
            Create(
                Fixture(mediaType: EuManifestationMediaType.PdfTypePdfa2a),
                EuAnnexBodyDispositionOutcome.TextNotAvailable)
                .IdentitySha256);
    }

    private static EuAnnexBodyDisposition Create(
        FixtureValues fixture,
        EuAnnexBodyDispositionOutcome outcome) =>
        EuAnnexBodyDisposition.Create(
            fixture.SourceObject,
            fixture.Location,
            fixture.Address,
            fixture.Request,
            fixture.Request,
            fixture.Evidence,
            fixture.Receipt,
            fixture.ProfileRef,
            outcome);

    private static FixtureValues Fixture(
        EuManifestationMediaType mediaType = EuManifestationMediaType.ApplicationPdf,
        string annexValue = "III",
        char byteFill = 'a',
        char profileFill = '8',
        string cellarKey = CellarKey,
        string? sourcePublisherUri = null,
        string? responseMediaType = null,
        bool includeExtraRequestHeader = false)
    {
        var sourceObject = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(ArtifactRef('1', '2'), "manifestation"),
            sourcePublisherUri ?? "http://publications.europa.eu/resource/cellar/" + cellarKey,
            cellarKey,
            Sha256(cellarKey),
            ArtifactRef('3', '4'),
            parentKeyRef: null);
        var location = EuStructuralLocation.Parse(
            "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} " + annexValue,
            AnnexAuthority);
        var address = EuDocumentFetchAddress.TryCreate(
            "cellar", cellarKey, mediaType, EuDocumentLanguage.Eng, out var refusal);
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);
        var admittedAddress = address!;

        var receipt = Receipt(Digest(byteFill), 3);
        var requestHeaders = new List<HttpLogicalRequestHeader>
        {
            new("accept", admittedAddress.Accept),
            new("accept-language", admittedAddress.AcceptLanguage),
        };
        if (includeExtraRequestHeader)
        {
            requestHeaders.Add(new HttpLogicalRequestHeader("x-extra", "not-part-of-the-address"));
        }

        var request = HttpLogicalRequest.Create(
            admittedAddress.ResourceUri,
            HttpRequestMethod.Get,
            requestHeaders,
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));
        var requestDigest = Sha256(request.CopyCanonicalBytes());
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            requestDigest,
            admittedAddress.ResourceUri,
            200,
            Headers(responseMediaType ?? admittedAddress.Accept),
            "2026-09-12T20:00:00.0000000Z",
            "2026-09-12T20:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(3),
            3,
            Digest(byteFill),
            DurableBlobWriteReceiptDigest.Of(receipt),
            3,
            Digest(byteFill));
        var evidence = RoutedHttpEvidence.Create(
            ArtifactRef('5', '6'),
            1,
            0,
            [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });

        return new FixtureValues(
            sourceObject,
            location,
            admittedAddress,
            request,
            evidence,
            receipt,
            ArtifactRef('7', profileFill));
    }

    private static RoutedHttpResponseHeaders Headers(string mediaType = "application/pdf")
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader(mediaType),
            new RoutedHttpSingleHeader("3"),
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent,
            absent);
    }

    private static RoutedHttpResponseHeaders RedirectHeaders(string location)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            absent,
            new RoutedHttpSingleHeader("0"),
            absent,
            absent,
            absent,
            absent,
            absent,
            new RoutedHttpSingleHeader(location),
            absent,
            absent,
            absent,
            absent,
            absent);
    }

    private static DurableBlobWriteReceipt Receipt(
        string digest,
        long length,
        string protectedAt = "2026-09-12T20:00:00Z")
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            digest,
            length,
            CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse(protectedAt),
                protectedUntil: null));
    }

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        Digest(digestFill));

    private static string Digest(char value) => new(value, 64);

    private static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool IsStaticMember(MemberInfo member) => member switch
    {
        MethodBase method => method.IsStatic,
        PropertyInfo property => (property.GetMethod ?? property.SetMethod)!.IsStatic,
        FieldInfo field => field.IsStatic,
        _ => false,
    };

    private sealed record FixtureValues(
        SourceObjectRef SourceObject,
        EuStructuralLocation Location,
        EuDocumentFetchAddress Address,
        HttpLogicalRequest Request,
        RoutedHttpEvidence Evidence,
        DurableBlobWriteReceipt Receipt,
        SourceArtifactRef ProfileRef);
}
