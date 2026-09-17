using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why the supplied evidence could not be bound as one annex population.</summary>
public enum EuAnnexEvidenceBindingRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,
    [JsonStringEnumMemberName("profile_digest_mismatch")]
    ProfileDigestMismatch = 1,
    [JsonStringEnumMemberName("profile_invalid")]
    ProfileInvalid = 2,
    [JsonStringEnumMemberName("profile_evidence_mismatch")]
    ProfileEvidenceMismatch = 3,
    [JsonStringEnumMemberName("source_evidence_missing_or_ambiguous")]
    SourceEvidenceMissingOrAmbiguous = 4,
    [JsonStringEnumMemberName("source_lineage_mismatch")]
    SourceLineageMismatch = 5,
    [JsonStringEnumMemberName("publisher_population_mismatch")]
    PublisherPopulationMismatch = 6,
    [JsonStringEnumMemberName("retained_pdf_unavailable")]
    RetainedPdfUnavailable = 7,
    [JsonStringEnumMemberName("pdf_unreadable")]
    PdfUnreadable = 8,
}

/// <summary>The unresolved part of one conserved Formex annex member.</summary>
public enum EuAnnexEvidenceGap
{
    [JsonStringEnumMemberName("body_classification_pending")]
    BodyClassificationPending = 1,
    [JsonStringEnumMemberName("publisher_page_labels_missing")]
    PublisherPageLabelsMissing = 2,
    [JsonStringEnumMemberName("publisher_page_labels_invalid")]
    PublisherPageLabelsInvalid = 3,
    [JsonStringEnumMemberName("publisher_page_label_missing")]
    PublisherPageLabelMissing = 4,
    [JsonStringEnumMemberName("publisher_page_label_duplicate")]
    PublisherPageLabelDuplicate = 5,
    [JsonStringEnumMemberName("pdf_pages_not_ordered_and_contiguous")]
    PdfPagesNotOrderedAndContiguous = 6,
    [JsonStringEnumMemberName("competing_pdf_page_claim")]
    CompetingPdfPageClaim = 7,
}

/// <summary>One publisher page label and the one-based physical PDF page it identifies.</summary>
public sealed record EuDerivedPdfPage(int PhysicalPageNumber, string PublisherPageLabel);

/// <summary>A complete mapping derived from Formex extent and retained PDF page labels.</summary>
public sealed class EuDerivedPdfPageMapping
{
    internal EuDerivedPdfPageMapping(IReadOnlyList<EuDerivedPdfPage> pages)
    {
        Pages = Array.AsReadOnly(pages.ToArray());
    }

    public string Derivation => "pdf_page_label_bijection/1";
    public IReadOnlyList<EuDerivedPdfPage> Pages { get; }
}

/// <summary>One lossless publisher annex identity and its mapping or classification gap.</summary>
public sealed class EuBoundAnnexEvidence
{
    internal EuBoundAnnexEvidence(
        EuFormexAnnexInventoryMember formex,
        EuXhtmlAnnexInventoryMember xhtml,
        EuDerivedPdfPageMapping? pdfMapping,
        EuAnnexEvidenceGap gap)
    {
        Formex = formex;
        Xhtml = xhtml;
        PdfMapping = pdfMapping;
        Gap = gap;
    }

    public EuFormexAnnexInventoryMember Formex { get; }
    public EuXhtmlAnnexInventoryMember Xhtml { get; }
    public EuDerivedPdfPageMapping? PdfMapping { get; }
    public EuAnnexEvidenceGap Gap { get; }
    public string PublisherAnnexId => Xhtml.PublisherAnnexId;
    public EuStructuralLocation? StructuralLocation => null;
}

/// <summary>The evidence retained by one complete Formex-member partition.</summary>
public sealed class EuAnnexEvidenceBinding
{
    internal EuAnnexEvidenceBinding(
        CorpusRecord workSource,
        SourceObjectRef publisherWork,
        EuFormexPackage package,
        SourceObjectRef pdfManifestation,
        EuFormexAnnexInventory formexInventory,
        EuXhtmlAnnexInventory xhtmlInventory,
        DurableBlobWriteReceipt pdfReceipt,
        SourceArtifactRef reconciliationProfileRef,
        IReadOnlyList<EuBoundAnnexEvidence> members)
    {
        WorkSource = workSource;
        Work = publisherWork;
        Expression = package.ExpressionRef;
        FormexManifestation = package.ManifestationRef;
        FormexBody = package.BodyRef;
        WorkCelex = package.WorkCelex;
        Language = package.Language;
        PdfManifestation = pdfManifestation;
        FormexSourceReceipt = formexInventory.SourceReceipt;
        FormexProfileRef = formexInventory.ProfileRef;
        FormexInventoryIdentitySha256 = formexInventory.IdentitySha256;
        XhtmlSourceReceipt = xhtmlInventory.SourceReceipt;
        XhtmlProfileRef = xhtmlInventory.ProfileRef;
        XhtmlInventoryIdentitySha256 = xhtmlInventory.IdentitySha256;
        PublisherWorkEli = xhtmlInventory.WorkEli;
        PdfReceipt = pdfReceipt;
        ReconciliationProfileRef = reconciliationProfileRef;
        Members = Array.AsReadOnly(members.ToArray());
        IdentitySha256 = IdentityOf(this);
    }

    public SourceObjectRef Work { get; }
    public SourceObjectRef Expression { get; }
    public SourceObjectRef FormexManifestation { get; }
    public SourceObjectRef FormexBody { get; }
    public string WorkCelex { get; }
    public string Language { get; }
    public CorpusRecord WorkSource { get; }
    public SourceObjectRef PdfManifestation { get; }
    public DurableBlobWriteReceipt FormexSourceReceipt { get; }
    public SourceArtifactRef FormexProfileRef { get; }
    public string FormexInventoryIdentitySha256 { get; }
    public DurableBlobWriteReceipt XhtmlSourceReceipt { get; }
    public SourceArtifactRef XhtmlProfileRef { get; }
    public string XhtmlInventoryIdentitySha256 { get; }
    public string PublisherWorkEli { get; }
    public DurableBlobWriteReceipt PdfReceipt { get; }
    public SourceArtifactRef ReconciliationProfileRef { get; }
    public IReadOnlyList<EuBoundAnnexEvidence> Members { get; }
    public string IdentitySha256 { get; }

    private static string IdentityOf(EuAnnexEvidenceBinding binding)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-annex-evidence-binding/1");
        Append(hash, binding.WorkSource.ObjectRef.CanonicalKeySha256);
        Append(hash, binding.Work.CanonicalKeySha256);
        Append(hash, binding.Expression.CanonicalKeySha256);
        Append(hash, binding.FormexManifestation.CanonicalKeySha256);
        Append(hash, binding.FormexBody.CanonicalKeySha256);
        Append(hash, binding.PdfManifestation.CanonicalKeySha256);
        Append(hash, binding.WorkCelex);
        Append(hash, binding.Language);
        Append(hash, binding.FormexInventoryIdentitySha256);
        Append(hash, DurableBlobWriteReceiptDigest.Of(binding.FormexSourceReceipt));
        Append(hash, binding.XhtmlInventoryIdentitySha256);
        Append(hash, DurableBlobWriteReceiptDigest.Of(binding.XhtmlSourceReceipt));
        Append(hash, binding.PublisherWorkEli);
        Append(hash, DurableBlobWriteReceiptDigest.Of(binding.PdfReceipt));
        Append(hash, binding.ReconciliationProfileRef.ResourceId);
        Append(hash, binding.ReconciliationProfileRef.Sha256);
        foreach (var member in binding.Members)
        {
            Append(hash, member.Formex.PackageEntry);
            Append(hash, member.Formex.Sequence);
            Append(hash, member.Formex.DocumentReferenceFile);
            Append(hash, member.Formex.DocumentReferenceValue);
            Append(hash, member.Formex.PageFirst.ToString(CultureInfo.InvariantCulture));
            Append(hash, member.Formex.PageLast.ToString(CultureInfo.InvariantCulture));
            Append(hash, member.Formex.PageTotal.ToString(CultureInfo.InvariantCulture));
            Append(hash, member.Formex.Title);
            Append(hash, member.Xhtml.PublisherUnitId);
            Append(hash, member.Xhtml.PublisherAnnexId);
            Append(hash, ((int)member.Gap).ToString(CultureInfo.InvariantCulture));
            foreach (var page in member.PdfMapping?.Pages ?? [])
            {
                Append(hash, page.PhysicalPageNumber.ToString(CultureInfo.InvariantCulture));
                Append(hash, page.PublisherPageLabel);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

/// <summary>One complete binding or one population-wide refusal.</summary>
public sealed class EuAnnexEvidenceBindingResult
{
    private EuAnnexEvidenceBindingResult(
        EuAnnexEvidenceBinding? binding,
        EuAnnexEvidenceBindingRefusal refusal,
        string? detail)
    {
        Binding = binding;
        Refusal = refusal;
        Detail = detail;
    }

    public EuAnnexEvidenceBinding? Binding { get; }
    public EuAnnexEvidenceBindingRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Bound => Refusal == EuAnnexEvidenceBindingRefusal.None;

    internal static EuAnnexEvidenceBindingResult Success(EuAnnexEvidenceBinding binding) =>
        new(binding, EuAnnexEvidenceBindingRefusal.None, null);

    internal static EuAnnexEvidenceBindingResult Refused(
        EuAnnexEvidenceBindingRefusal refusal,
        string detail) => new(null, refusal, detail);
}

/// <summary>
/// Binds receipt-proven Formex and XHTML annex identity to PDF page labels in the same verified
/// WEMI lineage. The public production door accepts no annex coordinate or page selection from a caller.
/// </summary>
public sealed class EuAnnexEvidenceBinder
{
    private const string ProfileHeader = "lex-v3-eu-annex-evidence-reconciliation-profile/1";
    private const string Rule = "rule=pdf_page_label_bijection/1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ICustodyStore _custodyStore;

    public EuAnnexEvidenceBinder(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public async Task<EuAnnexEvidenceBindingResult> RunAsync(
        EuWemiIdentityBoundary identityBoundary,
        EuFormexPackage package,
        SourceObjectRef expectedPdfManifestation,
        VerifiedCorpusRecordSet corpusRecordSet,
        EuFormexAnnexInventory formexInventory,
        EuXhtmlAnnexInventory xhtmlInventory,
        DurableBlobWriteReceipt retainedPdfBytes,
        ReadOnlyMemory<byte> reconciliationProfileBytes,
        SourceArtifactRef reconciliationProfileRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identityBoundary);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(expectedPdfManifestation);
        ArgumentNullException.ThrowIfNull(corpusRecordSet);
        ArgumentNullException.ThrowIfNull(formexInventory);
        ArgumentNullException.ThrowIfNull(xhtmlInventory);
        ArgumentNullException.ThrowIfNull(retainedPdfBytes);
        ArgumentNullException.ThrowIfNull(reconciliationProfileRef);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(CustodyDigest.Of(reconciliationProfileBytes.Span, cancellationToken),
                reconciliationProfileRef.Sha256, StringComparison.Ordinal))
        {
            return Refused(EuAnnexEvidenceBindingRefusal.ProfileDigestMismatch,
                "the reconciliation profile bytes do not carry the digest their reference names");
        }

        if (!TryReadProfile(reconciliationProfileBytes.Span, out var profile, out var failure))
        {
            return Refused(EuAnnexEvidenceBindingRefusal.ProfileInvalid, failure!);
        }

        if (!string.Equals(profile.FormexInventorySha256, formexInventory.IdentitySha256, StringComparison.Ordinal)
            || !string.Equals(profile.XhtmlInventorySha256, xhtmlInventory.IdentitySha256, StringComparison.Ordinal)
            || !string.Equals(profile.PdfTransportSha256,
                retainedPdfBytes.Reference.ContentSha256, StringComparison.Ordinal))
        {
            return Refused(EuAnnexEvidenceBindingRefusal.ProfileEvidenceMismatch,
                "the reconciliation profile names different inventory or PDF evidence");
        }

        if (formexInventory.TransportBinding.Expression != package.ExpressionRef
            || formexInventory.TransportBinding.FormexBody != package.BodyRef)
        {
            return Refused(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch,
                "the Formex inventory transport belongs to a different admitted package");
        }

        var expression = package.ExpressionRef;
        var heldSources = corpusRecordSet.Set.Records.Where(record =>
                record.Body.Kind == CorpusBodyRecordKind.Held
                && record.Body.Receipt == xhtmlInventory.SourceReceipt)
            .Take(2)
            .ToArray();
        if (heldSources.Length != 1)
        {
            return Refused(EuAnnexEvidenceBindingRefusal.SourceEvidenceMissingOrAmbiguous,
                "the retained XHTML inventory must be exactly one selected held work body");
        }
        var workSource = heldSources[0];
        var publisherWork = TryGetPublisherWork(identityBoundary, expression);

        if (!IsAdmitted(identityBoundary, package.BodyRef, EuWemiRole.Item)
            || !IsAdmitted(identityBoundary, package.ManifestationRef, EuWemiRole.Manifestation)
            || !IsAdmitted(identityBoundary, expression, EuWemiRole.Expression)
            || publisherWork is null
            || !string.Equals(
                workSource.ObjectRef.PublisherUri,
                publisherWork.PublisherUri,
                StringComparison.Ordinal)
            || !IsAdmitted(identityBoundary, expectedPdfManifestation, EuWemiRole.Manifestation)
            || !HasParent(package.ManifestationRef, expression)
            || !HasParent(package.BodyRef, package.ManifestationRef)
            || !HasParent(expectedPdfManifestation, expression))
        {
            return Refused(EuAnnexEvidenceBindingRefusal.SourceLineageMismatch,
                "the held work body, retained Formex package and PDF manifestation do not share the admitted work and expression lineage");
        }

        var xhtmlByEntry = xhtmlInventory.Members
            .GroupBy(static member => member.FormexPackageEntry, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        if (formexInventory.Members.Select(static member => member.PackageEntry)
                .Distinct(StringComparer.Ordinal).Count() != formexInventory.Members.Count
            || xhtmlInventory.Members.Select(static member => member.PublisherAnnexId)
                .Distinct(StringComparer.Ordinal).Count() != xhtmlInventory.Members.Count
            || xhtmlInventory.Members.Count != formexInventory.Members.Count
            || formexInventory.Members.Any(member =>
                !xhtmlByEntry.TryGetValue(member.PackageEntry, out var matches)
                || matches.Length != 1
                || !string.Equals(matches[0].Title, member.Title, StringComparison.Ordinal)))
        {
            return Refused(EuAnnexEvidenceBindingRefusal.PublisherPopulationMismatch,
                "Formex and XHTML do not identify the same complete annex population and titles");
        }

        ReadOnlyMemory<byte> pdfBytes;
        try
        {
            pdfBytes = await CustodyRestore.ReadCheckedAsync(
                _custodyStore, retainedPdfBytes.Reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CustodyRequiredException
            or CustodyIntegrityException or CustodyPolicyException)
        {
            return Refused(EuAnnexEvidenceBindingRefusal.RetainedPdfUnavailable, exception.Message);
        }

        PageLabelRead labels;
        try
        {
            labels = ReadPageLabelsFromBytes(pdfBytes.ToArray(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Refused(EuAnnexEvidenceBindingRefusal.PdfUnreadable, exception.GetType().Name);
        }

        var bound = new List<EuBoundAnnexEvidence>(formexInventory.Members.Count);
        foreach (var formex in formexInventory.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xhtml = xhtmlByEntry[formex.PackageEntry][0];
            var mapped = Map(formex, labels);
            bound.Add(new EuBoundAnnexEvidence(formex, xhtml, mapped.Mapping, mapped.Gap));
        }

        var claims = bound
            .SelectMany(static member => member.PdfMapping?.Pages.Select(page =>
                (Member: member, page.PhysicalPageNumber)) ?? [])
            .GroupBy(static claim => claim.PhysicalPageNumber)
            .Where(static group => group.Select(claim => claim.Member).Distinct().Count() > 1)
            .SelectMany(static group => group.Select(claim => claim.Member))
            .ToHashSet();
        if (claims.Count > 0)
        {
            bound = bound.Select(member => claims.Contains(member)
                ? new EuBoundAnnexEvidence(member.Formex, member.Xhtml, null,
                    EuAnnexEvidenceGap.CompetingPdfPageClaim)
                : member).ToList();
        }

        return EuAnnexEvidenceBindingResult.Success(new EuAnnexEvidenceBinding(
            workSource, publisherWork, package, expectedPdfManifestation, formexInventory,
            xhtmlInventory, retainedPdfBytes, reconciliationProfileRef, bound));
    }

    private static SourceObjectRef? TryGetPublisherWork(
        EuWemiIdentityBoundary boundary,
        SourceObjectRef expression)
    {
        if (expression.ParentKeyRef is not { } key)
        {
            return null;
        }

        try
        {
            var work = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Cellar,
                key.EntityKind,
                key.PublisherUri,
                key.CanonicalKey,
                key.CanonicalKeySha256,
                expression.IdentityProfileRef,
                null);
            return boundary.Require(work, EuWemiRole.Work, nameof(expression));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }


    private static bool HasParent(SourceObjectRef child, SourceObjectRef parent) =>
        child.ParentKeyRef is { } key
        && key.EntityKind == parent.EntityKind
        && string.Equals(key.CanonicalKey, parent.CanonicalKey, StringComparison.Ordinal)
        && string.Equals(key.PublisherUri, parent.PublisherUri, StringComparison.Ordinal);

    private static bool IsAdmitted(
        EuWemiIdentityBoundary boundary,
        SourceObjectRef source,
        EuWemiRole role)
    {
        try
        {
            boundary.Require(source, role, nameof(source));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static (EuDerivedPdfPageMapping? Mapping, EuAnnexEvidenceGap Gap) Map(
        EuFormexAnnexInventoryMember member,
        PageLabelRead labels)
    {
        if (labels.State == PageLabelState.Missing)
        {
            return (null, EuAnnexEvidenceGap.PublisherPageLabelsMissing);
        }
        if (labels.State == PageLabelState.Invalid)
        {
            return (null, EuAnnexEvidenceGap.PublisherPageLabelsInvalid);
        }

        var pages = new List<EuDerivedPdfPage>(member.PageTotal);
        for (var number = member.PageFirst; number <= member.PageLast; number++)
        {
            var expected = number.ToString(CultureInfo.InvariantCulture);
            var matches = labels.Labels!
                .Where(page => string.Equals(page.PublisherPageLabel, expected, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                return (null, EuAnnexEvidenceGap.PublisherPageLabelMissing);
            }
            if (matches.Length != 1)
            {
                return (null, EuAnnexEvidenceGap.PublisherPageLabelDuplicate);
            }
            pages.Add(matches[0]);
        }

        if (pages.Select(static page => page.PhysicalPageNumber)
                .Zip(pages.Skip(1).Select(static page => page.PhysicalPageNumber),
                    static (left, right) => right == left + 1).Any(static contiguous => !contiguous))
        {
            return (null, EuAnnexEvidenceGap.PdfPagesNotOrderedAndContiguous);
        }

        return (new EuDerivedPdfPageMapping(pages), EuAnnexEvidenceGap.BodyClassificationPending);
    }

    private static PageLabelRead ReadPageLabelsFromBytes(
        byte[] pdfBytes,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(pdfBytes);
        return ReadPageLabels(document, cancellationToken);
    }

    private static PageLabelRead ReadPageLabels(object documentValue, CancellationToken cancellationToken)
    {
        var document = (PdfDocument)documentValue;
        if (!document.Structure.Catalog.CatalogDictionary.TryGet(
                NameToken.Create("PageLabels"), out var root))
        {
            return new PageLabelRead(PageLabelState.Missing, null);
        }

        var entries = new SortedDictionary<int, object>();
        var path = new HashSet<string>(StringComparer.Ordinal);
        if (!ReadNumberTree(document, root, entries, path, 0, cancellationToken)
            || entries.Count == 0 || entries.Keys.First() != 0)
        {
            return new PageLabelRead(PageLabelState.Invalid, null);
        }

        var starts = entries.Keys.ToArray();
        var pages = new List<EuDerivedPdfPage>(document.NumberOfPages);
        for (var physical = 1; physical <= document.NumberOfPages; physical++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var zeroBased = physical - 1;
            var start = starts.Last(value => value <= zeroBased);
            if (!TryLabel(entries[start], zeroBased - start, out var label))
            {
                return new PageLabelRead(PageLabelState.Invalid, null);
            }
            pages.Add(new EuDerivedPdfPage(physical, label!));
        }
        return new PageLabelRead(PageLabelState.Valid, pages);
    }

    private static bool ReadNumberTree(
        object documentValue,
        object tokenValue,
        SortedDictionary<int, object> entries,
        HashSet<string> path,
        int depth,
        CancellationToken cancellationToken)
    {
        var document = (PdfDocument)documentValue;
        if (tokenValue is not IToken token)
        {
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 32)
        {
            return false;
        }

        string? nodeIdentity = null;
        if (token is IndirectReferenceToken indirect)
        {
            nodeIdentity = indirect.Data.ToString();
            if (!path.Add(nodeIdentity))
            {
                return false;
            }
        }

        try
        {
            return ReadNumberTreeNode(document, token, entries, path, depth, cancellationToken);
        }
        finally
        {
            if (nodeIdentity is not null)
            {
                path.Remove(nodeIdentity);
            }
        }
    }

    private static bool ReadNumberTreeNode(
        object documentValue,
        object tokenValue,
        SortedDictionary<int, object> entries,
        HashSet<string> path,
        int depth,
        CancellationToken cancellationToken)
    {
        var document = (PdfDocument)documentValue;
        if (tokenValue is not IToken token
            || !TryResolve(document, token, out var resolved)
            || resolved is not DictionaryToken dictionary)
        {
            return false;
        }

        var hasNums = dictionary.TryGet(NameToken.Create("Nums"), out var numsToken);
        var hasKids = dictionary.TryGet(NameToken.Create("Kids"), out var kidsToken);
        if (hasNums == hasKids)
        {
            return false;
        }

        if (hasNums)
        {
            if (!TryResolve(document, numsToken!, out var resolvedNums)
                || resolvedNums is not ArrayToken nums || nums.Data.Count == 0
                || nums.Data.Count % 2 != 0)
            {
                return false;
            }
            for (var index = 0; index < nums.Data.Count; index += 2)
            {
                if (!TryResolve(document, nums.Data[index], out var key)
                    || key is not NumericToken number || number.Int < 0
                    || !TryResolve(document, nums.Data[index + 1], out var value)
                    || value is not DictionaryToken spec || !entries.TryAdd(number.Int, spec))
                {
                    return false;
                }
            }
            return true;
        }

        if (!TryResolve(document, kidsToken!, out var resolvedKids)
            || resolvedKids is not ArrayToken kids || kids.Data.Count == 0)
        {
            return false;
        }
        foreach (var kid in kids.Data)
        {
            if (!ReadNumberTree(document, kid, entries, path, depth + 1, cancellationToken))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryResolve(
        object documentValue,
        object tokenValue,
        out object resolved)
    {
        var document = (PdfDocument)documentValue;
        if (tokenValue is not IToken token)
        {
            resolved = tokenValue;
            return false;
        }
        resolved = token;
        if (token is not IndirectReferenceToken indirect)
        {
            return true;
        }
        resolved = document.Structure.GetObject(indirect.Data).Data;
        return true;
    }

    private static bool TryLabel(object specification, int offset, out string? label)
    {
        label = null;
        if (specification is not DictionaryToken spec)
        {
            return false;
        }
        var prefix = spec.TryGet(NameToken.Create("P"), out StringToken? prefixToken)
            && prefixToken is not null ? prefixToken.Data : string.Empty;
        var start = spec.TryGet(NameToken.Create("St"), out NumericToken? startToken)
            && startToken is not null ? startToken.Int : 1;
        if (start <= 0 || offset > int.MaxValue - start)
        {
            return false;
        }
        if (!spec.TryGet(NameToken.Create("S"), out NameToken? style) || style is null)
        {
            label = prefix;
            return prefix.Length > 0;
        }

        var value = start + offset;
        var suffix = style.Data switch
        {
            "D" => value.ToString(CultureInfo.InvariantCulture),
            "r" => Roman(value)?.ToLowerInvariant(),
            "R" => Roman(value),
            "a" => Alpha(value)?.ToLowerInvariant(),
            "A" => Alpha(value),
            _ => null,
        };
        if (suffix is null)
        {
            return false;
        }
        label = prefix + suffix;
        return true;
    }

    private static string? Alpha(int value)
    {
        const int MaximumLabelLength = 4096;
        var length = ((value - 1) / 26) + 1;
        if (value <= 0 || length > MaximumLabelLength)
        {
            return null;
        }
        var letter = (char)('A' + ((value - 1) % 26));
        return new string(letter, length);
    }

    private static string? Roman(int value)
    {
        if (value is <= 0 or > 3999)
        {
            return null;
        }
        var values = new (int Value, string Text)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"),
            (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"),
            (5, "V"), (4, "IV"), (1, "I"),
        };
        var result = new StringBuilder();
        foreach (var item in values)
        {
            while (value >= item.Value)
            {
                result.Append(item.Text);
                value -= item.Value;
            }
        }
        return result.ToString();
    }

    private static bool TryReadProfile(
        ReadOnlySpan<byte> bytes,
        out ReconciliationProfile profile,
        out string? failure)
    {
        profile = default;
        failure = null;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            failure = "the reconciliation profile is not UTF-8";
            return false;
        }
        var lines = text.Split('\n');
        if (lines.Length != 6 || lines[^1].Length != 0
            || !string.Equals(lines[0], ProfileHeader, StringComparison.Ordinal)
            || !lines[1].StartsWith("formex_inventory_sha256=", StringComparison.Ordinal)
            || !lines[2].StartsWith("xhtml_inventory_sha256=", StringComparison.Ordinal)
            || !lines[3].StartsWith("pdf_transport_sha256=", StringComparison.Ordinal)
            || !string.Equals(lines[4], Rule, StringComparison.Ordinal))
        {
            failure = "the reconciliation profile does not have the exact admitted shape";
            return false;
        }
        profile = new ReconciliationProfile(
            lines[1]["formex_inventory_sha256=".Length..],
            lines[2]["xhtml_inventory_sha256=".Length..],
            lines[3]["pdf_transport_sha256=".Length..]);
        if (!CustodyDigest.IsLowercaseSha256(profile.FormexInventorySha256)
            || !CustodyDigest.IsLowercaseSha256(profile.XhtmlInventorySha256)
            || !CustodyDigest.IsLowercaseSha256(profile.PdfTransportSha256))
        {
            failure = "the reconciliation profile does not name three lowercase SHA-256 values";
            return false;
        }
        return true;
    }

    private static EuAnnexEvidenceBindingResult Refused(
        EuAnnexEvidenceBindingRefusal refusal,
        string detail) => EuAnnexEvidenceBindingResult.Refused(refusal, detail);

    private readonly record struct ReconciliationProfile(
        string FormexInventorySha256,
        string XhtmlInventorySha256,
        string PdfTransportSha256);
    private readonly record struct PageLabelRead(
        PageLabelState State,
        IReadOnlyList<EuDerivedPdfPage>? Labels);
    private enum PageLabelState { Missing, Invalid, Valid }
}
