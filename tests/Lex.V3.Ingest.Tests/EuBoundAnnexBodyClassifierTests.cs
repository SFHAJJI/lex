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
public sealed class EuBoundAnnexBodyClassifierTests
{
    [TestMethod]
    public async Task BoundImagePagesProduceDeterministicTextNotAvailable()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var first = await fixture.RunAsync();
        var second = await fixture.RunAsync();

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.None, first.Refusal, first.Detail);
        Assert.AreEqual(first.Classification!.IdentitySha256,
            second.Classification!.IdentitySha256);
        Assert.AreEqual(fixture.Binding.IdentitySha256,
            first.Classification.Binding.IdentitySha256);
        Assert.AreEqual(fixture.Address.ArtifactRef, first.Classification.OfficialAddress.ArtifactRef);
        Assert.AreEqual(fixture.Binding.PdfReceipt, first.Classification.PdfReceipt);
        Assert.HasCount(1, first.Classification.Members);
        var member = first.Classification.Members[0];
        Assert.AreSame(fixture.Binding.Members[0], member.Evidence);
        Assert.AreEqual(EuAnnexBodyDispositionOutcome.TextNotAvailable, member.Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.None, member.Gap);
        Assert.AreEqual("image_only", member.ReasonCode);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuBoundAnnexBodyMemberClassification>)first.Classification.Members).Clear());
    }

    [TestMethod]
    public async Task MappingGapIsConservedWithoutInventingAClassification()
    {
        var fixture = await FixtureAsync(image: false, text: false, pageLabels: false);

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.None, result.Refusal, result.Detail);
        var member = result.Classification!.Members.Single();
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelsMissing, member.Evidence.Gap);
        Assert.IsNull(member.Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.MappingUnresolved, member.Gap);
        Assert.AreEqual("mapping_unresolved", member.ReasonCode);
    }

    [TestMethod]
    public async Task MappingGapNeverReopensOrInspectsThePdf()
    {
        var fixture = await FixtureAsync(image: false, text: false, pageLabels: false);

        var result = await fixture.RunAsync(
            store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.None, result.Refusal, result.Detail);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.MappingUnresolved,
            result.Classification!.Members.Single().Gap);
    }

    [TestMethod]
    public async Task TextAndBlankMappedPagesCannotBecomeImageOnly()
    {
        foreach (var (image, text, expected) in new[]
        {
            (true, true, EuBoundAnnexBodyClassificationGap.BodyContainsText),
            (false, false, EuBoundAnnexBodyClassificationGap.BodyContainsNoImage),
        })
        {
            var fixture = await FixtureAsync(image, text);
            var member = (await fixture.RunAsync()).Classification!.Members.Single();
            Assert.IsNull(member.Outcome);
            Assert.AreEqual(expected, member.Gap);
        }
    }

    [TestMethod]
    public async Task OneGlyphCannotBecomeImageOnly()
    {
        var fixture = await FixtureAsync(image: true, text: true, textValue: "x");

        var member = (await fixture.RunAsync()).Classification!.Members.Single();

        Assert.IsNull(member.Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.BodyContainsText, member.Gap);
    }

    [TestMethod]
    public async Task ProfileCannotNameAnotherBindingOrPdf()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var wrongBinding = Profile(new string('a', 64),
            fixture.Binding.PdfReceipt.Reference.ContentSha256);
        var wrongPdf = Profile(fixture.Binding.IdentitySha256, new string('b', 64));

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.ProfileEvidenceMismatch,
            (await fixture.RunAsync(wrongBinding)).Refusal);
        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.ProfileEvidenceMismatch,
            (await fixture.RunAsync(wrongPdf)).Refusal);
        var badRef = fixture.Profile with
        {
            Reference = new SourceArtifactRef(fixture.Profile.Reference.ResourceId, new string('c', 64)),
        };
        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.ProfileDigestMismatch,
            (await fixture.RunAsync(badRef)).Refusal);

        var malformedBytes = Encoding.UTF8.GetBytes("not-a-classification-profile\n");
        var malformed = new ProfileValue(malformedBytes, Artifact('f', Sha(malformedBytes)));
        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.ProfileInvalid,
            (await fixture.RunAsync(malformed)).Refusal);

        var wrongRule = Profile(fixture.Binding.IdentitySha256,
            fixture.Binding.PdfReceipt.Reference.ContentSha256,
            "classification=pdfpig-0.1.11:no_glyphs");
        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.ProfileInvalid,
            (await fixture.RunAsync(wrongRule)).Refusal);
    }

    [TestMethod]
    public async Task MappedMemberRequiresTheExactRetainedPdf()
    {
        var fixture = await FixtureAsync(image: true, text: false);

        var result = await fixture.RunAsync(
            store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore());

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.RetainedPdfUnavailable,
            result.Refusal);
        Assert.IsNull(result.Classification);
    }

    [TestMethod]
    public async Task EveryInputMemberIsConservedOnceInStableOrder()
    {
        var fixture = await FixtureAsync(
            image: true, text: false, pageLabels: true, twoMembers: true,
            secondMemberAfterFirst: true);

        var result = await fixture.RunAsync();

        CollectionAssert.AreEqual(new[] { "anx_1", "anx_2" },
            result.Classification!.Members
                .Select(static member => member.Evidence.PublisherAnnexId).ToArray());
        Assert.AreEqual(EuAnnexBodyDispositionOutcome.TextNotAvailable,
            result.Classification.Members[0].Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.None,
            result.Classification.Members[0].Gap);
        Assert.IsNull(result.Classification.Members[1].Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.MappingUnresolved,
            result.Classification.Members[1].Gap);
    }

    [TestMethod]
    public async Task MappedPageOutsideTheRetainedDocumentIsConservedAsAGap()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var member = fixture.Binding.Members.Single();
        var outside = new EuBoundAnnexEvidence(member.Formex, member.Xhtml,
            new EuDerivedPdfPageMapping([new EuDerivedPdfPage(8, "8")]),
            EuAnnexEvidenceGap.BodyClassificationPending);
        fixture = fixture.WithMembers([outside]);

        var result = await fixture.RunAsync();

        Assert.IsNull(result.Classification!.Members.Single().Outcome);
        Assert.AreEqual(EuBoundAnnexBodyClassificationGap.MappedPageOutsideDocument,
            result.Classification.Members.Single().Gap);
    }

    [TestMethod]
    public async Task OfficialRouteMustNameTheBoundWorkAndReceipt()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var wrongRoute = Route(new string('1', 36), fixture.Binding.PdfReceipt, fixture.PdfLength);

        var result = await fixture.RunAsync(route: wrongRoute);

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.SourceEvidenceMismatch,
            result.Refusal);
        Assert.IsNull(result.Classification);
    }

    [TestMethod]
    public async Task OfficialRequestCanonicalDigestMustNameTheFirstHop()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var substituted = Request(fixture.Address, new string('3', 64));

        var result = await fixture.RunAsync(officialRequest: substituted);

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.SourceEvidenceMismatch,
            result.Refusal);
        Assert.IsNull(result.Classification);
    }

    [TestMethod]
    public async Task TerminalReceiptDigestMustNameTheBoundPdfReceipt()
    {
        var fixture = await FixtureAsync(image: true, text: false);
        var original = fixture.Binding.PdfReceipt;
        var policy = original.PolicyEvidence;
        var substitutedPolicy = new CustodyPolicyEvidence(
            policy.Schema, policy.Reference, policy.VerificationProfile, policy.PolicyKey,
            policy.Protection, policy.ObservedAt.AddSeconds(1), policy.ProtectedUntil);
        var substitutedReceipt = new DurableBlobWriteReceipt(
            original.Schema, original.Reference, substitutedPolicy);
        var substitutedRoute = Route(
            fixture.Binding.Work.CanonicalKey, substitutedReceipt, fixture.PdfLength);

        var result = await fixture.RunAsync(route: substitutedRoute);

        Assert.AreEqual(EuBoundAnnexBodyClassificationRefusal.SourceEvidenceMismatch,
            result.Refusal);
        Assert.IsNull(result.Classification);
    }

    [TestMethod]
    public void PublicDoorAcceptsNoAnnexCoordinateOrPageSelection()
    {
        var parameters = typeof(EuBoundAnnexBodyClassifier)
            .GetMethod(nameof(EuBoundAnnexBodyClassifier.RunAsync))!
            .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();

        Assert.IsFalse(parameters.Contains(typeof(EuStructuralLocation)));
        Assert.IsFalse(parameters.Contains(typeof(int)));
        Assert.IsFalse(parameters.Contains(typeof(int[])));
        Assert.IsFalse(parameters.Contains(typeof(IReadOnlyList<int>)));
        Assert.IsTrue(parameters.Contains(typeof(EuAnnexEvidenceBinding)));
    }

    private static async Task<Fixture> FixtureAsync(
        bool image, bool text, bool pageLabels = true, bool twoMembers = false,
        bool secondMemberAfterFirst = false, string textValue = "text")
    {
        var pdf = EuAnnexEvidenceBinderTests.PageLabelPdf(
            7, pageLabels ? "<< /S /D /St 1 >>" : null, image: image, text: text,
            textValue: textValue);
        var binderFixture = await EuAnnexEvidenceBinderTests.FixtureAsync(
            pdf, formexTwoMembers: twoMembers, xhtmlTwoMembers: twoMembers,
            secondMemberAfterFirst: secondMemberAfterFirst);
        var bindingResult = await binderFixture.RunAsync();
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.None, bindingResult.Refusal,
            bindingResult.Detail);
        var binding = bindingResult.Binding!;
        var route = Route(binding.Work.CanonicalKey, binding.PdfReceipt, pdf.Length);
        return new Fixture(binderFixture.Store, binderFixture.Package, binderFixture.Formex,
            binderFixture.Xhtml, binding, route.Address, route.Request, route.Evidence,
            Profile(binding.IdentitySha256,
                binding.PdfReceipt.Reference.ContentSha256), pdf.Length);
    }

    internal static RouteValues Route(
        string cellarKey, DurableBlobWriteReceipt receipt, int length)
    {
        var address = EuDocumentFetchAddress.TryCreate(
            "cellar", cellarKey, EuManifestationMediaType.ApplicationPdf,
            EuDocumentLanguage.Eng, out var refusal)!;
        Assert.AreEqual(EuDocumentFetchAddressRefusal.None, refusal);
        var request = Request(address, new string('2', 64));
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            Sha(request.CopyCanonicalBytes()),
            address.ResourceUri,
            200,
            Headers(address.Accept, checked((ulong)length)),
            "2026-09-14T12:00:00.0000000Z",
            "2026-09-14T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(checked((ulong)length)),
            checked((ulong)length),
            receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt),
            checked((ulong)length),
            receipt.Reference.ContentSha256);
        var evidence = RoutedHttpEvidence.Create(
            Artifact('d', new string('d', 64)), 1, 0, [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
        return new RouteValues(address, request, evidence);
    }

    private static HttpLogicalRequest Request(EuDocumentFetchAddress address, string contextDigest) =>
        HttpLogicalRequest.Create(
            address.ResourceUri,
            HttpRequestMethod.Get,
            [
                new HttpLogicalRequestHeader("accept", address.Accept),
                new HttpLogicalRequestHeader("accept-language", address.AcceptLanguage),
            ],
            new HttpLogicalRequestBody(0, Sha([])),
            new string('1', 64),
            contextDigest);

    internal static ProfileValue Profile(string bindingIdentity, string pdfDigest,
        string rule = "classification=pdfpig-0.1.11:no_glyphs+image")
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-bound-annex-body-classification-profile/1",
            "binding_identity_sha256=" + bindingIdentity,
            "pdf_transport_sha256=" + pdfDigest,
            rule) + "\n");
        return new(bytes, Artifact('e', Sha(bytes)));
    }

    private static RoutedHttpResponseHeaders Headers(string mediaType, ulong length)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new(new RoutedHttpSingleHeader(mediaType),
            new RoutedHttpSingleHeader(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
    }

    private static SourceArtifactRef Artifact(char fill, string digest) => new(
        $"urn:uuid:{new string(fill, 8)}-{new string(fill, 4)}-4{new string(fill, 3)}-8{new string(fill, 3)}-{new string(fill, 12)}",
        digest);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal sealed record ProfileValue(byte[] Bytes, SourceArtifactRef Reference);
    internal sealed record RouteValues(
        EuDocumentFetchAddress Address, HttpLogicalRequest Request, RoutedHttpEvidence Evidence);
    private sealed record Fixture(
        ICustodyStore Store,
        EuFormexPackage Package,
        EuFormexAnnexInventory Formex,
        EuXhtmlAnnexInventory Xhtml,
        EuAnnexEvidenceBinding Binding,
        EuDocumentFetchAddress Address,
        HttpLogicalRequest Request,
        RoutedHttpEvidence Evidence,
        ProfileValue Profile,
        int PdfLength)
    {
        internal Task<EuBoundAnnexBodyClassificationResult> RunAsync(
            ProfileValue? profile = null, RouteValues? route = null, ICustodyStore? store = null,
            HttpLogicalRequest? officialRequest = null,
            HttpLogicalRequest? terminalRequest = null) =>
            new EuBoundAnnexBodyClassifier(store ?? Store).RunAsync(
                Binding, route?.Address ?? Address, officialRequest ?? route?.Request ?? Request,
                terminalRequest ?? route?.Request ?? Request, route?.Evidence ?? Evidence,
                (profile ?? Profile).Bytes, (profile ?? Profile).Reference,
                CancellationToken.None);

        internal Fixture WithMembers(IReadOnlyList<EuBoundAnnexEvidence> members)
        {
            var binding = new EuAnnexEvidenceBinding(
                Binding.WorkSource, Package, Binding.PdfManifestation,
                Formex, Xhtml, Binding.PdfReceipt,
                Binding.ReconciliationProfileRef, members);
            var route = Route(binding.Work.CanonicalKey, binding.PdfReceipt, PdfLength);
            return this with
            {
                Binding = binding,
                Address = route.Address,
                Request = route.Request,
                Evidence = route.Evidence,
                Profile = EuBoundAnnexBodyClassifierTests.Profile(binding.IdentitySha256,
                    binding.PdfReceipt.Reference.ContentSha256),
            };
        }
    }
}
