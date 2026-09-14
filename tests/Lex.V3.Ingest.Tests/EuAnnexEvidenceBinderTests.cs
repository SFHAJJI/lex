using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
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
    public async Task SubstitutedPdfLineageIsRefusedBeforeReadingPages()
    {
        var fixture = await FixtureAsync(
            PageLabelPdf(7, "<< /S /D /St 1 >>"), pdfInOtherExpression: true);

        var result = await fixture.RunAsync();

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
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
            fixture.Package, fixture.Corpus, fixture.Formex, duplicated, fixture.PdfReceipt,
            profile.Bytes, profile.Reference, CancellationToken.None);

        Assert.AreEqual(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch, result.Refusal);
        Assert.IsNull(result.Binding);
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
        var fixture = await FixtureAsync(PageLabelPdf(7, "<< /S /A /St 2147483647 >>"));

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
    }

    private static async Task<Fixture> FixtureAsync(
        byte[] pdfBytes,
        bool pdfInOtherExpression = false,
        string xhtmlTitle = "ANNEX",
        bool formexTwoMembers = false,
        bool xhtmlTwoMembers = false)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var formexBytes = FormexPackage(formexTwoMembers);
        var xhtmlBytes = Encoding.UTF8.GetBytes(Xhtml(xhtmlTitle, xhtmlTwoMembers));
        var formexReceipt = await Hold(store, formexBytes);
        var xhtmlReceipt = await Hold(store, xhtmlBytes);
        var pdfReceipt = await Hold(store, pdfBytes);

        var formexProfile = InventoryProfile(
            "lex-v3-eu-formex-annex-inventory-profile/1", formexReceipt, "annex_root=ANNEX", '8');
        var xhtmlProfile = InventoryProfile(
            "lex-v3-eu-xhtml-annex-inventory-profile/1", xhtmlReceipt,
            "xhtml_namespace=http://www.w3.org/1999/xhtml", '9');
        var formex = (await new EuFormexAnnexInventoryProducer(store).RunAsync(
            formexReceipt, formexProfile.Bytes, formexProfile.Reference, CancellationToken.None)).Inventory!;
        var xhtml = (await new EuXhtmlAnnexInventoryProducer(store).RunAsync(
            xhtmlReceipt, xhtmlProfile.Bytes, xhtmlProfile.Reference, CancellationToken.None)).Inventory!;

        var boundary = new EuWemiIdentityBoundary(Registry, IdentityProfile);
        const string workKey = "5f2552c2-11bd-11e6-ba9a-01aa75ed71a1";
        var work = Object(workKey, EuWemiRole.Work, null);
        var expression = Object(workKey + ".0001", EuWemiRole.Expression, work);
        var formexManifestation = Object(workKey + ".0001.01", EuWemiRole.Manifestation, expression);
        var formexItem = Object(workKey + ".0001.01/FORMEX", EuWemiRole.Item, formexManifestation);
        var xhtmlManifestation = Object(workKey + ".0001.02", EuWemiRole.Manifestation, expression);
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

        var all = new List<(SourceObjectRef Object, DurableBlobWriteReceipt Receipt)>
        {
            (work, await Hold(store, "work"u8.ToArray())),
            (expression, await Hold(store, "expression"u8.ToArray())),
            (formexManifestation, await Hold(store, "formex-manifestation"u8.ToArray())),
            (formexItem, formexReceipt),
            (xhtmlManifestation, await Hold(store, "xhtml-manifestation"u8.ToArray())),
            (xhtmlItem, xhtmlReceipt),
        };
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
        return new Fixture(store, package, corpus, work, formex, xhtml, pdfReceipt, profile);
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

    private static byte[] ProfileBytes(string formex, string xhtml, string pdf) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            "lex-v3-eu-annex-evidence-reconciliation-profile/1",
            "formex_inventory_sha256=" + formex,
            "xhtml_inventory_sha256=" + xhtml,
            "pdf_transport_sha256=" + pdf,
            "rule=pdf_page_label_bijection/1") + "\n");

    private static byte[] FormexPackage(bool twoMembers)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "L_202601965EN.doc.fmx.xml", DocumentXml());
            Write(archive, "L_202601965EN.000201.fmx.xml", AnnexXml("0001.0001"));
            if (twoMembers)
            {
                Write(archive, "L_202601965EN.000202.fmx.xml", AnnexXml("0001.0002"));
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

    private static string AnnexXml(string sequence) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ANNEX xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xsi:noNamespaceSchemaLocation="http://formex.publications.europa.eu/schema/formex-test.xd">
          <BIB.INSTANCE>
            <DOCUMENT.REF FILE="L_202601965EN.doc.fmx.xml">LEU20261965EN1101</DOCUMENT.REF>
            <NO.SEQ>{{sequence}}</NO.SEQ><PAGE.FIRST>2</PAGE.FIRST>
            <PAGE.LAST>7</PAGE.LAST><PAGE.TOTAL>6</PAGE.TOTAL>
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

    private static byte[] PageLabelPdf(
        int pageCount,
        string? pageLabelSpecification,
        bool rawTree = false)
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
        objects.AddRange(pageObjects.Select(_ =>
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>"));
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

    private sealed record Profile(byte[] Bytes, SourceArtifactRef Reference);

    private sealed record Fixture(
        ICustodyStore Store,
        EuFormexPackage Package,
        VerifiedCorpusRecordSet Corpus,
        SourceObjectRef Work,
        EuFormexAnnexInventory Formex,
        EuXhtmlAnnexInventory Xhtml,
        DurableBlobWriteReceipt PdfReceipt,
        Profile Profile)
    {
        internal Task<EuAnnexEvidenceBindingResult> RunAsync(Profile? profile = null) =>
            new EuAnnexEvidenceBinder(Store).RunAsync(
                Package, Corpus, Formex, Xhtml, PdfReceipt,
                (profile ?? Profile).Bytes, (profile ?? Profile).Reference, CancellationToken.None);
    }
}
