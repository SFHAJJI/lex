using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuAnnexEvidenceBinderTests
{
    [TestMethod]
    public async Task ExactEvidenceProducesLosslessIdentityAndDerivedMapping()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.None, result.Refusal, result.Detail);
        var binding = result.Binding!;
        Assert.AreEqual(fixture.Work, binding.Work);
        Assert.AreEqual(fixture.Package.ExpressionRef, binding.Expression);
        Assert.AreEqual(fixture.Package.ManifestationRef, binding.FormexManifestation);
        Assert.AreEqual(fixture.Package.BodyRef, binding.FormexBody);
        Assert.AreEqual(fixture.Formex.SourceReceipt, binding.FormexSourceReceipt);
        Assert.AreEqual(fixture.Xhtml.SourceReceipt, binding.XhtmlSourceReceipt);
        Assert.AreEqual(fixture.Formex.IdentitySha256, binding.FormexInventoryIdentitySha256);
        Assert.AreEqual(fixture.Xhtml.IdentitySha256, binding.XhtmlInventoryIdentitySha256);
        Assert.AreEqual(fixture.Xhtml.WorkEli, binding.PublisherWorkEli);
        Assert.AreEqual(fixture.PdfReceipt, binding.PdfReceipt);
        Assert.AreEqual(fixture.Profile.Reference, binding.ReconciliationProfileRef);
        Assert.AreEqual(1, binding.Members.Count);
        var member = binding.Members[0];
        Assert.AreEqual("L_202601965EN.000201.fmx.xml", member.Formex.PackageEntry);
        Assert.AreEqual("0001.0001", member.Formex.Sequence);
        Assert.AreEqual("LEU20261965EN1101", member.Formex.DocumentReferenceValue);
        Assert.AreEqual("ANNEX", member.Formex.Title);
        Assert.AreEqual("L_202601965EN.000201.fmx", member.Xhtml.PublisherUnitId);
        Assert.AreEqual("anx_1", member.PublisherAnnexId);
        Assert.IsNull(member.StructuralLocation);
        Assert.AreEqual(EuAnnexEvidenceGap.BodyClassificationPending, member.Gap);
        CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6, 7 },
            member.PdfMapping!.Pages.Select(static page => page.PhysicalPageNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "2", "3", "4", "5", "6", "7" },
            member.PdfMapping.Pages.Select(static page => page.PublisherPageLabel).ToArray());
        Assert.AreEqual("pdf_page_label_bijection/1", member.PdfMapping.Derivation);
    }

    [TestMethod]
    public async Task RepeatedRunIsDeterministicAndExposedCollectionsAreImmutable()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));

        var first = (await fixture.RunAsync()).Binding!;
        var second = (await fixture.RunAsync()).Binding!;

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuBoundAnnexEvidence>)first.Members).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<EuDerivedPdfPage>)first.Members[0].PdfMapping!.Pages).Clear());
    }

    [TestMethod]
    public async Task ProfileCannotSubstituteAnyEvidenceInput()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var wrong = fixture.Profile with
        {
            Bytes = ProfileBytes(new string('a', 64), fixture.Xhtml.IdentitySha256,
                fixture.PdfReceipt.Reference.ContentSha256),
        };
        wrong = wrong with { Reference = Artifact('f', Sha(wrong.Bytes)) };

        var result = await fixture.RunAsync(wrong);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.ProfileEvidenceMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task ProfileDigestShapeRuleAndPdfTransportAreExact()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var wrongDigest = fixture.Profile with
        {
            Reference = new SourceArtifactRef(fixture.Profile.Reference.ResourceId, new string('0', 64)),
        };
        var badHeaderBytes = fixture.Profile.Bytes.ToArray();
        badHeaderBytes[0] = (byte)'X';
        var badHeader = new Profile(badHeaderBytes, Artifact('4', Sha(badHeaderBytes)));
        var badRuleBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Profile.Bytes)
            .Replace("rule=pdf_page_label_bijection/1", "rule=pdf_page_label_bijection/2",
                StringComparison.Ordinal));
        var badRule = new Profile(badRuleBytes, Artifact('5', Sha(badRuleBytes)));
        var wrongPdfBytes = ProfileBytes(fixture.Formex.IdentitySha256,
            fixture.Xhtml.IdentitySha256, new string('6', 64));
        var wrongPdf = new Profile(wrongPdfBytes, Artifact('6', Sha(wrongPdfBytes)));

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.ProfileDigestMismatch,
            (await fixture.RunAsync(wrongDigest)).Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.ProfileInvalid,
            (await fixture.RunAsync(badHeader)).Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.ProfileInvalid,
            (await fixture.RunAsync(badRule)).Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.ProfileEvidenceMismatch,
            (await fixture.RunAsync(wrongPdf)).Refusal);
    }

    [TestMethod]
    public async Task MissingDuplicateAndUnavailablePdfEvidenceAreRefused()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var sources = Sources(fixture);
        var missing = VerifiedCorpus(sources.Where(source => source.Receipt != fixture.PdfReceipt).ToArray());
        var duplicateObject = Object("duplicate-pdf", EuWemiRole.Item,
            fixture.Corpus.Set.Records.Single(record =>
                record.ObjectRef.CanonicalKey.EndsWith(".0001.03", StringComparison.Ordinal)).ObjectRef);
        var ambiguous = VerifiedCorpus([.. sources, (duplicateObject, fixture.PdfReceipt)]);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceEvidenceMissingOrAmbiguous,
            (await fixture.RunAsync(corpus: missing)).Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceEvidenceMissingOrAmbiguous,
            (await fixture.RunAsync(corpus: ambiguous)).Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.RetainedPdfUnavailable,
            (await fixture.RunAsync(store: new EuAcquisitionTestFixture.EuInMemoryCustodyStore())).Refusal);
    }

    [TestMethod]
    public async Task UnreadableRetainedPdfIsRefused()
    {
        var fixture = await FixtureAsync("not a pdf"u8.ToArray());

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PdfUnreadable, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task SubstitutedPdfLineageIsRefusedBeforeReadingPages()
    {
        var fixture = await FixtureAsync(
            PageLabelPdf(7, "<< /S /D /St 1 >>"), pdfInOtherExpression: true);

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task SubstitutedXhtmlLineageIsRefusedBeforeReadingPages()
    {
        var fixture = await FixtureAsync(
            PageLabelPdf(7, "<< /S /D /St 1 >>"), xhtmlInOtherExpression: true);

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task FormexInventoryMustNameTheAdmittedPackageBody()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var otherReceipt = await Hold(fixture.Store, "other formex"u8.ToArray());
        var otherBody = Object("other-formex", EuWemiRole.Item, fixture.Package.ManifestationRef);
        var corpus = VerifiedCorpus([.. Sources(fixture), (otherBody, otherReceipt)]);
        var formex = new EuFormexAnnexInventory(
            new EuFormexAnnexTransportBinding(
                fixture.Package,
                FormexRequest(fixture.Package.BodyRef),
                FormexResponse(
                    FormexRequest(fixture.Package.BodyRef), otherReceipt),
                otherReceipt),
            fixture.Formex.ProfileRef,
            fixture.Formex.Members);
        var profile = ReconciliationProfile(formex, fixture.Xhtml, fixture.PdfReceipt, '3');

        var result = await fixture.RunAsync(profile, corpus, formex);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task FormexInventoryTransportMustNameTheSameExpressionAsThePackage()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var otherExpression = Object(
            fixture.Work.CanonicalKey + ".0002", EuWemiRole.Expression, fixture.Work);
        var otherManifestation = Object(
            otherExpression.CanonicalKey + ".01", EuWemiRole.Manifestation, otherExpression);
        var otherItem = Object(
            otherManifestation.CanonicalKey + "/FORMEX", EuWemiRole.Item, otherManifestation);
        var stream = EuFormexStreamName.TryParse(
            "CL2026R1965EN0000010.0001.xml", "32026R1965", out var roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var items = EuFormexItemSet.TryAdmit(
            [new EuFormexItem(fixture.Boundary, stream, otherItem, 0)], out roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var otherPackage = EuFormexPackage.TryAdmit(
            fixture.Boundary, otherManifestation, otherExpression, items, "EN",
            out var packageRefusal)!;
        Assert.AreEqual(EuFormexPackageRefusal.None, packageRefusal);
        var request = FormexRequest(otherPackage.BodyRef);
        var transport = new EuFormexAnnexTransportBinding(
            otherPackage,
            request,
            FormexResponse(request, fixture.Formex.SourceReceipt),
            fixture.Formex.SourceReceipt);
        var formex = new EuFormexAnnexInventory(
            transport, fixture.Formex.ProfileRef, fixture.Formex.Members);
        var profile = ReconciliationProfile(formex, fixture.Xhtml, fixture.PdfReceipt, '2');

        var result = await fixture.RunAsync(profile: profile, formex: formex);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task WorkMustBeAdmittedByTheExpressionIdentityBoundary()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var badWork = new SourceObjectRef(SourceCoreSchemaIds.SourceObjectRef,
            fixture.Work.Authority, fixture.Work.EntityKind, fixture.Work.PublisherUri,
            fixture.Work.CanonicalKey, fixture.Work.CanonicalKeySha256,
            Artifact('2', new string('2', 64)), fixture.Work.ParentKeyRef);
        var sources = Sources(fixture).Select(source => source.Object == fixture.Work
            ? (badWork, source.Receipt) : source).ToArray();

        var result = await fixture.RunAsync(corpus: VerifiedCorpus(sources));

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task SuppliedIdentityBoundaryReAdmitsThePackageExpression()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var hostileBoundary = new EuWemiIdentityBoundary(
            Registry, Artifact('2', new string('2', 64)));

        var result = await fixture.RunAsync(boundary: hostileBoundary);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task EvidenceLegsMustBeDistinctAndPdfMustMatchTheExpectedManifestation()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var collapsedProfile = ReconciliationProfile(
            fixture.Formex, fixture.Xhtml, fixture.Formex.SourceReceipt, '4');
        var collapsed = await fixture.RunAsync(
            collapsedProfile, pdfReceipt: fixture.Formex.SourceReceipt);

        var decoyReceipt = await Hold(fixture.Store, PageLabelPdf(9, "<< /S /D /St 1 >>"));
        var decoyManifestation = Object(
            fixture.Package.ExpressionRef.CanonicalKey + ".04",
            EuWemiRole.Manifestation, fixture.Package.ExpressionRef);
        var decoyItem = Object(decoyManifestation.CanonicalKey + "/PDF",
            EuWemiRole.Item, decoyManifestation);
        var corpus = VerifiedCorpus([
            .. Sources(fixture),
            (decoyManifestation, await Hold(fixture.Store, "decoy manifestation"u8.ToArray())),
            (decoyItem, decoyReceipt),
        ]);
        var decoyProfile = ReconciliationProfile(
            fixture.Formex, fixture.Xhtml, decoyReceipt, '5');
        var decoy = await fixture.RunAsync(decoyProfile, corpus,
            pdfReceipt: decoyReceipt);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, collapsed.Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, decoy.Refusal);
    }

    [TestMethod]
    public async Task TitleDisagreementCannotAlterTheFormexPopulation()
    {
        var fixture = await FixtureAsync(
            PageLabelPdf(7, "<< /S /D /St 1 >>"), xhtmlTitle: "CORROBORATING TITLE");

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task MissingOrExtraXhtmlMemberCannotAlterTheFormexPopulation()
    {
        var missing = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"),
            formexTwoMembers: true);
        var extra = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"),
            xhtmlTwoMembers: true);

        var missingResult = await missing.RunAsync();
        var extraResult = await extra.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch,
            missingResult.Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch,
            extraResult.Refusal);
    }

    [TestMethod]
    public async Task DuplicatePackageEntryClaimCannotBind()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"));
        var duplicated = new EuXhtmlAnnexInventory(
            fixture.Xhtml.SourceReceipt, fixture.Xhtml.ProfileRef, fixture.Xhtml.WorkEli,
            [fixture.Xhtml.Members[0], fixture.Xhtml.Members[0] with { PublisherAnnexId = "anx_2" }]);
        var bytes = ProfileBytes(fixture.Formex.IdentitySha256, duplicated.IdentitySha256,
            fixture.PdfReceipt.Reference.ContentSha256);
        var profile = new Profile(bytes, Artifact('7', Sha(bytes)));

        var result = await new EuAnnexEvidenceBinder(fixture.Store).RunAsync(
            fixture.Boundary, fixture.Package, fixture.PdfManifestation,
            fixture.Corpus, fixture.Formex, duplicated, fixture.PdfReceipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
    }

    [TestMethod]
    public async Task DuplicateFormexEntryOrPublisherAnnexIdCannotConserveThePopulation()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /D /St 1 >>"),
            formexTwoMembers: true, xhtmlTwoMembers: true);
        var duplicateFormex = new EuFormexAnnexInventory(
            fixture.Formex.TransportBinding, fixture.Formex.ProfileRef,
            [fixture.Formex.Members[0], fixture.Formex.Members[1] with
                { PackageEntry = fixture.Formex.Members[0].PackageEntry }]);
        var duplicateXhtml = new EuXhtmlAnnexInventory(
            fixture.Xhtml.SourceReceipt, fixture.Xhtml.ProfileRef, fixture.Xhtml.WorkEli,
            [fixture.Xhtml.Members[0], fixture.Xhtml.Members[1] with
                { PublisherAnnexId = fixture.Xhtml.Members[0].PublisherAnnexId }]);

        var formexResult = await fixture.RunAsync(
            ReconciliationProfile(duplicateFormex, fixture.Xhtml, fixture.PdfReceipt, '1'),
            formex: duplicateFormex);
        var xhtmlResult = await fixture.RunAsync(
            ReconciliationProfile(fixture.Formex, duplicateXhtml, fixture.PdfReceipt, '2'),
            xhtml: duplicateXhtml);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch,
            formexResult.Refusal);
        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch,
            xhtmlResult.Refusal);
    }

    [TestMethod]
    public async Task MissingPageLabelsConservesMemberWithTypedGapAndNoPages()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, pageLabelSpecification: null));

        var result = await fixture.RunAsync();

        var member = result.Binding!.Members.Single();
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelsMissing, member.Gap);
        Assert.IsNull(member.PdfMapping);
    }

    [TestMethod]
    public async Task UnreasonablyLargeAlphabeticPageLabelIsInvalidAndSelectsNoPages()
    {
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /A /St 200000 >>"));

        var result = await fixture.RunAsync();

        var member = result.Binding!.Members.Single();
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelsInvalid, member.Gap);
        Assert.IsNull(member.PdfMapping);
    }

    [TestMethod]
    public async Task DuplicatePublisherLabelsProduceTypedGapAndNoPages()
    {
        var fixture = await FixtureAsync(PageLabelPdf(
            7, "<< /Nums [0 << /S /D /St 1 >> 4 << /S /D /St 2 >>] >>", rawTree: true));

        var result = await fixture.RunAsync();

        var member = result.Binding!.Members.Single();
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelDuplicate, member.Gap);
        Assert.IsNull(member.PdfMapping);
    }

    [TestMethod]
    public async Task NonIdentityPublisherLabelsMapToTheirPhysicalPages()
    {
        var fixture = await FixtureAsync(PageLabelPdf(
            8, "<< /Nums [0 << /S /r /St 1 >> 2 << /S /D /St 2 >>] >>", rawTree: true));

        var member = (await fixture.RunAsync()).Binding!.Members.Single();

        Assert.AreEqual(EuAnnexEvidenceGap.BodyClassificationPending, member.Gap);
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8 },
            member.PdfMapping!.Pages.Select(static page => page.PhysicalPageNumber).ToArray());
    }

    [TestMethod]
    public async Task NumberTreeMustBeginAtZeroAndMayShareAnIndirectSpecification()
    {
        var missingZero = await FixtureAsync(PageLabelPdf(
            7, "<< /Nums [1 << /S /D /St 1 >>] >>", rawTree: true));
        var sharedObjectNumber = 10;
        var shared = await FixtureAsync(PageLabelPdf(7,
            $"<< /Nums [0 {sharedObjectNumber} 0 R 4 {sharedObjectNumber} 0 R] >>",
            rawTree: true, additionalObjects: ["<< /S /D /St 1 >>"]));

        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelsInvalid,
            (await missingZero.RunAsync()).Binding!.Members.Single().Gap);
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelDuplicate,
            (await shared.RunAsync()).Binding!.Members.Single().Gap);
    }

    [TestMethod]
    public async Task RetainedPublisherSpecimensConserveOneRawMemberWithoutGuessingPages()
    {
        var fixture = await FixtureAsync(
            await FixtureBytesAsync("new-pdfa2a-200-body.bin"),
            formexBytes: await FixtureBytesAsync("new-fmx4-200-body.bin"),
            xhtmlBytes: await FixtureBytesAsync("new-xhtml-200-body.bin"));

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.None, result.Refusal, result.Detail);
        var member = result.Binding!.Members.Single();
        Assert.AreEqual("anx_1", member.PublisherAnnexId);
        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelsMissing, member.Gap);
        Assert.IsNull(member.PdfMapping);
    }

    [TestMethod]
    public async Task IncompleteOrNoncontiguousLabelsSelectNoPages()
    {
        var incomplete = await FixtureAsync(PageLabelPdf(
            7, "<< /Nums [0 << /S /D /St 1 >> 4 << /P (x) >>] >>", rawTree: true));
        var noncontiguous = await FixtureAsync(PageLabelPdf(
            7, "<< /Nums [0 << /S /D /St 2 >> 1 << /S /D /St 9 >> 2 << /S /D /St 3 >>] >>",
            rawTree: true));

        var incompleteMember = (await incomplete.RunAsync()).Binding!.Members.Single();
        var noncontiguousMember = (await noncontiguous.RunAsync()).Binding!.Members.Single();

        Assert.AreEqual(EuAnnexEvidenceGap.PublisherPageLabelMissing, incompleteMember.Gap);
        Assert.IsNull(incompleteMember.PdfMapping);
        Assert.AreEqual(EuAnnexEvidenceGap.PdfPagesNotOrderedAndContiguous, noncontiguousMember.Gap);
        Assert.IsNull(noncontiguousMember.PdfMapping);
    }

    [TestMethod]
    public async Task CompetingMembersAreBothUnresolvedNeverFirstWins()
    {
        var fixture = await FixtureAsync(
            PageLabelPdf(7, "<< /S /D /St 1 >>"),
            formexTwoMembers: true, xhtmlTwoMembers: true);

        var result = await fixture.RunAsync();

        Assert.AreEqual(2, result.Binding!.Members.Count);
        Assert.IsTrue(result.Binding.Members.All(static member =>
            member.Gap == EuAnnexEvidenceGap.CompetingPdfPageClaim
            && member.PdfMapping is null));
        CollectionAssert.AreEqual(new[] { "anx_1", "anx_2" },
            result.Binding.Members.Select(static member => member.PublisherAnnexId).ToArray());
    }

    [TestMethod]
    public void PublicDoorAcceptsNoCoordinateOrPageSelection()
    {
        var run = typeof(EuAnnexEvidenceBinder).GetMethod(nameof(EuAnnexEvidenceBinder.RunAsync))!;
        var parameterTypes = run.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();

        Assert.IsFalse(parameterTypes.Contains(typeof(EuStructuralLocation)));
        Assert.IsFalse(parameterTypes.Contains(typeof(int)));
        Assert.IsFalse(parameterTypes.Contains(typeof(int[])));
        Assert.IsFalse(parameterTypes.Contains(typeof(IReadOnlyList<int>)));
        Assert.IsTrue(parameterTypes.Contains(typeof(EuWemiIdentityBoundary)));
    }

    internal static async Task<Fixture> FixtureAsync(
        byte[] pdfBytes,
        bool pdfInOtherExpression = false,
        bool xhtmlInOtherExpression = false,
        string xhtmlTitle = "ANNEX",
        bool formexTwoMembers = false,
        bool xhtmlTwoMembers = false,
        bool secondMemberAfterFirst = false,
        byte[]? formexBytes = null,
        byte[]? xhtmlBytes = null)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        formexBytes ??= FormexPackage(formexTwoMembers, secondMemberAfterFirst);
        xhtmlBytes ??= Encoding.UTF8.GetBytes(Xhtml(xhtmlTitle, xhtmlTwoMembers));
        var formexReceipt = await Hold(store, formexBytes);
        var xhtmlReceipt = await Hold(store, xhtmlBytes);
        var pdfReceipt = await Hold(store, pdfBytes);

        var formexProfile = FormexProfile('8');
        var xhtmlProfile = InventoryProfile(
            "lex-v3-eu-xhtml-annex-inventory-profile/1", xhtmlReceipt,
            "xhtml_namespace=http://www.w3.org/1999/xhtml", '9');
        var xhtml = (await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            xhtmlReceipt, xhtmlProfile.Bytes, xhtmlProfile.Reference, CancellationToken.None)).Inventory!;

        var boundary = new EuWemiIdentityBoundary(Registry, IdentityProfile);
        const string workKey = "5f2552c2-11bd-11e6-ba9a-01aa75ed71a1";
        var work = Object(workKey, EuWemiRole.Work, null);
        var expression = Object(workKey + ".0001", EuWemiRole.Expression, work);
        var formexManifestation = Object(workKey + ".0001.01", EuWemiRole.Manifestation, expression);
        var formexItem = Object(workKey + ".0001.01/FORMEX", EuWemiRole.Item, formexManifestation);
        var xhtmlExpression = xhtmlInOtherExpression
            ? Object(workKey + ".0003", EuWemiRole.Expression, work) : expression;
        var xhtmlManifestation = Object(
            xhtmlInOtherExpression ? workKey + ".0003.01" : workKey + ".0001.02",
            EuWemiRole.Manifestation, xhtmlExpression);
        var xhtmlItem = Object(workKey + ".0001.02/XHTML", EuWemiRole.Item, xhtmlManifestation);
        var pdfExpression = pdfInOtherExpression
            ? Object(workKey + ".0002", EuWemiRole.Expression, work) : expression;
        var pdfManifestation = Object(
            pdfInOtherExpression ? workKey + ".0002.01" : workKey + ".0001.03",
            EuWemiRole.Manifestation, pdfExpression);
        var pdfItem = Object(pdfManifestation.CanonicalKey + "/PDF", EuWemiRole.Item, pdfManifestation);

        var stream = EuFormexStreamName.TryParse(
            "CL2026R1965EN0000010.0001.xml", "32026R1965", out var roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var items = EuFormexItemSet.TryAdmit(
            [new EuFormexItem(boundary, stream, formexItem, 0)], out roleRefusal)!;
        Assert.AreEqual(EuFormexRoleRefusal.None, roleRefusal);
        var package = EuFormexPackage.TryAdmit(
            boundary, formexManifestation, expression, items, "EN", out var packageRefusal)!;
        Assert.AreEqual(EuFormexPackageRefusal.None, packageRefusal);
        var formexRequest = FormexRequest(package.BodyRef);
        var formexTransport = new EuFormexAnnexTransportBinding(
            package,
            formexRequest,
            FormexResponse(formexRequest, formexReceipt),
            formexReceipt);
        var formex = (await new EuFormexAnnexInventoryProducer(store).RunAsync(
            formexTransport, formexProfile.Bytes, formexProfile.Reference,
            CancellationToken.None)).Inventory!;

        var all = new List<(SourceObjectRef Object, DurableBlobWriteReceipt Receipt)>
        {
            (work, await Hold(store, "work"u8.ToArray())),
            (expression, await Hold(store, "expression"u8.ToArray())),
            (formexManifestation, await Hold(store, "formex-manifestation"u8.ToArray())),
            (formexItem, formexReceipt),
            (xhtmlManifestation, await Hold(store, "xhtml-manifestation"u8.ToArray())),
            (xhtmlItem, xhtmlReceipt),
        };
        if (xhtmlInOtherExpression)
        {
            all.Insert(4, (xhtmlExpression, await Hold(store, "other-xhtml-expression"u8.ToArray())));
        }
        if (pdfInOtherExpression)
        {
            all.Add((pdfExpression, await Hold(store, "other-expression"u8.ToArray())));
        }
        all.Add((pdfManifestation, await Hold(store, "pdf-manifestation"u8.ToArray())));
        all.Add((pdfItem, pdfReceipt));
        var corpus = VerifiedCorpus(all);
        var profileBytes = ProfileBytes(
            formex.IdentitySha256, xhtml.IdentitySha256, pdfReceipt.Reference.ContentSha256);
        var profile = new Profile(profileBytes, Artifact('f', Sha(profileBytes)));
        return new Fixture(store, boundary, package, pdfManifestation, corpus,
            work, formex, xhtml, pdfReceipt, profile);
    }

    private static VerifiedCorpusRecordSet VerifiedCorpus(
        IReadOnlyList<(SourceObjectRef Object, DurableBlobWriteReceipt Receipt)> sources)
    {
        var manifest = Artifact('a', new string('a', 64));
        var run = Artifact('b', new string('b', 64));
        var records = sources.Select((source, index) => new CorpusRecord(
            CorpusRecordSchemaIds.Record, source.Object, index,
            ScopeDisposition.AcceptedSelected, ScopeDisposition.AcceptedSelected,
            ScopeDisposition.AcceptedSelected, ScopeDisposition.AcceptedSelected,
            CorpusBodyRecord.Held(source.Receipt), manifest, run)).ToArray();
        var set = new CorpusRecordSet(CorpusRecordSetSchemaIds.Set, manifest, run, records);
        using var bytes = new MemoryStream();
        var digest = CorpusRecordSetCanonicalWriter.Write(bytes, set);
        return VerifiedCorpusRecordSet.ParseAndVerify(Artifact('c', digest), bytes.ToArray());
    }

    private static (SourceObjectRef Object, DurableBlobWriteReceipt Receipt)[] Sources(Fixture fixture) =>
        fixture.Corpus.Set.Records.Select(static record => (record.ObjectRef, record.Body.Receipt!)).ToArray();

    private static Profile ReconciliationProfile(
        EuFormexAnnexInventory formex,
        EuXhtmlAnnexInventory xhtml,
        DurableBlobWriteReceipt pdf,
        char resource)
    {
        var bytes = ProfileBytes(formex.IdentitySha256, xhtml.IdentitySha256,
            pdf.Reference.ContentSha256);
        return new Profile(bytes, Artifact(resource, Sha(bytes)));
    }

    private static Task<byte[]> FixtureBytesAsync(string name) => File.ReadAllBytesAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name));

    private static SourceObjectRef Object(string key, EuWemiRole role, SourceObjectRef? parent)
    {
        var kind = new SourceRegistryMemberRef(Registry, EuWemiIdentityBoundary.MemberKeyOf(role));
        var parentKey = parent is null ? null : new SourceObjectKeyRef(
            parent.EntityKind, parent.PublisherUri, parent.CanonicalKey, parent.CanonicalKeySha256);
        return new SourceObjectRef(SourceCoreSchemaIds.SourceObjectRef, SourceAuthority.Cellar, kind,
            "http://publications.europa.eu/resource/cellar/" + key, key, Sha(Encoding.UTF8.GetBytes(key)),
            IdentityProfile, parentKey);
    }

    private static async Task<DurableBlobWriteReceipt> Hold(
        ICustodyStore store,
        byte[] bytes) => await store.CreateAsync(
            bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);

    private static Profile InventoryProfile(
        string header,
        DurableBlobWriteReceipt receipt,
        string lastLine,
        char resource)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', header,
            "transport_sha256=" + receipt.Reference.ContentSha256, lastLine) + "\n");
        return new Profile(bytes, Artifact(resource, Sha(bytes)));
    }

    private static Profile FormexProfile(char resource)
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
        return new Profile(bytes, Artifact(resource, Sha(bytes)));
    }

    private static HttpLogicalRequest FormexRequest(SourceObjectRef body) =>
        HttpLogicalRequest.Create(
            "https://publications.europa.eu/resource/cellar/" + body.CanonicalKey,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("accept", "application/zip;mtype=fmx4")],
            new HttpLogicalRequestBody(0, Sha([])),
            new string('1', 64),
            new string('2', 64));

    private static RoutedHttpEvidence FormexResponse(
        HttpLogicalRequest request,
        DurableBlobWriteReceipt receipt)
    {
        var absent = new RoutedHttpAbsentHeader();
        var headers = new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader("application/zip;mtype=fmx4"),
            new RoutedHttpSingleHeader(receipt.Reference.ByteLength.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
        var length = checked((ulong)receipt.Reference.ByteLength);
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            Sha(request.CopyCanonicalBytes()),
            request.Uri,
            200,
            headers,
            "2026-09-14T12:00:00.0000000Z",
            "2026-09-14T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(length),
            length,
            receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt),
            length,
            receipt.Reference.ContentSha256);
        return RoutedHttpEvidence.Create(
            Artifact('7', new string('7', 64)), 1, 0, [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = receipt });
    }

    private static byte[] ProfileBytes(string formex, string xhtml, string pdf) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-annex-evidence-reconciliation-profile/1",
            "formex_inventory_sha256=" + formex,
            "xhtml_inventory_sha256=" + xhtml,
            "pdf_transport_sha256=" + pdf,
            "rule=pdf_page_label_bijection/1") + "\n");

    private static byte[] FormexPackage(bool twoMembers, bool secondMemberAfterFirst)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "L_202601965EN.doc.fmx.xml", DocumentXml());
            Write(archive, "L_202601965EN.000201.fmx.xml", AnnexXml("0001.0001"));
            if (twoMembers)
            {
                Write(archive, "L_202601965EN.000202.fmx.xml", secondMemberAfterFirst
                    ? AnnexXml("0001.0002", 8, 13)
                    : AnnexXml("0001.0002"));
            }
        }
        return bytes.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string xml)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    private static string DocumentXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <DOC xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             xsi:noNamespaceSchemaLocation="http://formex.publications.europa.eu/schema/formex-test.xd"/>
        """;

    private static string AnnexXml(string sequence, int pageFirst = 2, int pageLast = 7) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ANNEX xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xsi:noNamespaceSchemaLocation="http://formex.publications.europa.eu/schema/formex-test.xd">
          <BIB.INSTANCE>
            <DOCUMENT.REF FILE="L_202601965EN.doc.fmx.xml">LEU20261965EN1101</DOCUMENT.REF>
            <NO.SEQ>{{sequence}}</NO.SEQ><PAGE.FIRST>{{pageFirst}}</PAGE.FIRST>
            <PAGE.LAST>{{pageLast}}</PAGE.LAST><PAGE.TOTAL>{{pageLast - pageFirst + 1}}</PAGE.TOTAL>
          </BIB.INSTANCE><TITLE><TI><P>ANNEX</P></TI></TITLE>
        </ANNEX>
        """;

    private static string Xhtml(string title, bool twoMembers) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body>
          <div id="L_202601965EN.000201.fmx"><div class="eli-container" id="anx_1"><p class="oj-doc-ti">{{title}}</p></div></div>
          {{(twoMembers ? "<div id=\"L_202601965EN.000202.fmx\"><div class=\"eli-container\" id=\"anx_2\"><p class=\"oj-doc-ti\">ANNEX</p></div></div>" : string.Empty)}}
          <p>ELI: http://data.europa.eu/eli/reg_impl/2026/1965/oj</p>
        </body></html>
        """;

    internal static byte[] PageLabelPdf(
        int pageCount,
        string? pageLabelSpecification,
        bool rawTree = false,
        IReadOnlyList<string>? additionalObjects = null,
        bool image = false,
        bool text = false,
        string textValue = "text")
    {
        var pageObjects = Enumerable.Range(3, pageCount).ToArray();
        var catalogLabels = pageLabelSpecification is null ? string.Empty
            : " /PageLabels " + (rawTree
                ? pageLabelSpecification
                : "<< /Nums [0 " + pageLabelSpecification + "] >>");
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R{catalogLabels} >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pageObjects.Select(static number => $"{number} 0 R"))}] /Count {pageCount} >>",
        };
        var nextObject = pageObjects[^1] + 1;
        var imageObject = image ? nextObject++ : 0;
        var fontObject = text ? nextObject++ : 0;
        var contentObject = nextObject;
        var resources = string.Join(' ', new[]
        {
            image ? $"/XObject << /Im0 {imageObject} 0 R >>" : string.Empty,
            text ? $"/Font << /F1 {fontObject} 0 R >>" : string.Empty,
        }.Where(static value => value.Length > 0));
        var pageSuffix = image || text
            ? $" /Resources << {resources} >> /Contents {contentObject} 0 R"
            : string.Empty;
        objects.AddRange(pageObjects.Select(_ =>
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100]{pageSuffix} >>"));
        if (image)
        {
            objects.Add("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\nX\nendstream");
        }
        if (text)
        {
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        }
        if (image || text)
        {
            var commands = (image ? "q 1 0 0 1 0 0 cm /Im0 Do Q\n" : string.Empty)
                + (text ? $"BT /F1 12 Tf 0 0 Td ({textValue}) Tj ET\n" : string.Empty);
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(commands)} >>\nstream\n{commands}endstream");
        }
        if (additionalObjects is not null)
        {
            objects.AddRange(additionalObjects);
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

    private static readonly SourceArtifactRef Registry = Artifact('d', new string('d', 64));
    private static readonly SourceArtifactRef IdentityProfile = Artifact('e', new string('e', 64));

    private static SourceArtifactRef Artifact(char fill, string digest) => new(
        $"urn:uuid:{new string(fill, 8)}-{new string(fill, 4)}-4{new string(fill, 3)}-8{new string(fill, 3)}-{new string(fill, 12)}",
        digest);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal sealed record Profile(byte[] Bytes, SourceArtifactRef Reference);

    internal sealed record Fixture(
        ICustodyStore Store,
        EuWemiIdentityBoundary Boundary,
        EuFormexPackage Package,
        SourceObjectRef PdfManifestation,
        VerifiedCorpusRecordSet Corpus,
        SourceObjectRef Work,
        EuFormexAnnexInventory Formex,
        EuXhtmlAnnexInventory Xhtml,
        DurableBlobWriteReceipt PdfReceipt,
        Profile Profile)
    {
        internal Task<EuAnnexEvidenceBindingResult> RunAsync(
            Profile? profile = null,
            VerifiedCorpusRecordSet? corpus = null,
            EuFormexAnnexInventory? formex = null,
            EuXhtmlAnnexInventory? xhtml = null,
            ICustodyStore? store = null,
            EuWemiIdentityBoundary? boundary = null,
            SourceObjectRef? expectedPdfManifestation = null,
            DurableBlobWriteReceipt? pdfReceipt = null) =>
            new EuAnnexEvidenceBinder(store ?? Store).RunAsync(
                boundary ?? Boundary, Package, expectedPdfManifestation ?? PdfManifestation,
                corpus ?? Corpus, formex ?? Formex, xhtml ?? Xhtml, pdfReceipt ?? PdfReceipt,
                (profile ?? Profile).Bytes, (profile ?? Profile).Reference, CancellationToken.None);
    }
}
