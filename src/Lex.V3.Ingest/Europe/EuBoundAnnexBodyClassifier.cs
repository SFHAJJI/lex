using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using UglyToad.PdfPig;

namespace Lex.V3.Ingest.Europe;

public enum EuBoundAnnexBodyClassificationRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,
    [JsonStringEnumMemberName("profile_digest_mismatch")]
    ProfileDigestMismatch = 1,
    [JsonStringEnumMemberName("profile_invalid")]
    ProfileInvalid = 2,
    [JsonStringEnumMemberName("profile_evidence_mismatch")]
    ProfileEvidenceMismatch = 3,
    [JsonStringEnumMemberName("source_evidence_mismatch")]
    SourceEvidenceMismatch = 4,
    [JsonStringEnumMemberName("retained_pdf_unavailable")]
    RetainedPdfUnavailable = 5,
    [JsonStringEnumMemberName("pdf_unreadable")]
    PdfUnreadable = 6,
}

public enum EuBoundAnnexBodyClassificationGap
{
    [JsonStringEnumMemberName("none")]
    None = 0,
    [JsonStringEnumMemberName("mapping_unresolved")]
    MappingUnresolved = 1,
    [JsonStringEnumMemberName("body_contains_text")]
    BodyContainsText = 2,
    [JsonStringEnumMemberName("body_contains_no_image")]
    BodyContainsNoImage = 3,
    [JsonStringEnumMemberName("mapped_page_outside_document")]
    MappedPageOutsideDocument = 4,
}

public sealed class EuBoundAnnexBodyMemberClassification
{
    internal EuBoundAnnexBodyMemberClassification(
        EuBoundAnnexEvidence evidence,
        EuAnnexBodyDispositionOutcome? outcome,
        EuBoundAnnexBodyClassificationGap gap)
    {
        Evidence = evidence;
        Outcome = outcome;
        Gap = gap;
        SemanticIdentitySha256 = IdentityOf(this);
    }

    public EuBoundAnnexEvidence Evidence { get; }
    public EuAnnexBodyDispositionOutcome? Outcome { get; }
    public EuBoundAnnexBodyClassificationGap Gap { get; }
    public string SemanticIdentitySha256 { get; }
    public string ReasonCode => Outcome == EuAnnexBodyDispositionOutcome.TextNotAvailable
        ? "image_only"
        : Gap switch
        {
            EuBoundAnnexBodyClassificationGap.MappingUnresolved => "mapping_unresolved",
            EuBoundAnnexBodyClassificationGap.BodyContainsText => "body_contains_text",
            EuBoundAnnexBodyClassificationGap.BodyContainsNoImage => "body_contains_no_image",
            EuBoundAnnexBodyClassificationGap.MappedPageOutsideDocument =>
                "mapped_page_outside_document",
            _ => throw new InvalidOperationException("The member has no body outcome or gap."),
        };

    private static string IdentityOf(EuBoundAnnexBodyMemberClassification member)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        EuBoundAnnexBodyClassification.Append(hash,
            "lex-v3-eu-bound-annex-body-member-classification/1");
        EuBoundAnnexBodyClassification.Append(hash, member.Evidence.Formex.PackageEntry);
        EuBoundAnnexBodyClassification.Append(hash, member.Evidence.Formex.Sequence);
        EuBoundAnnexBodyClassification.Append(hash, member.Evidence.PublisherAnnexId);
        EuBoundAnnexBodyClassification.Append(hash,
            ((int?)member.Outcome)?.ToString(CultureInfo.InvariantCulture) ?? "");
        EuBoundAnnexBodyClassification.Append(hash,
            ((int)member.Gap).ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

public sealed class EuBoundAnnexBodyClassification
{
    internal EuBoundAnnexBodyClassification(
        EuAnnexEvidenceBinding binding,
        EuDocumentFetchAddress officialAddress,
        RepresentationChainObservation sourceObservation,
        SourceArtifactRef profileRef,
        IReadOnlyList<EuBoundAnnexBodyMemberClassification> members)
    {
        Binding = binding;
        OfficialAddress = officialAddress;
        SourceObservation = sourceObservation;
        PdfReceipt = binding.PdfReceipt;
        ProfileRef = profileRef;
        Members = Array.AsReadOnly(members.ToArray());
        IdentitySha256 = IdentityOf(this);
    }

    public EuAnnexEvidenceBinding Binding { get; }
    public EuDocumentFetchAddress OfficialAddress { get; }
    public RepresentationChainObservation SourceObservation { get; }
    public DurableBlobWriteReceipt PdfReceipt { get; }
    public SourceArtifactRef ProfileRef { get; }
    public IReadOnlyList<EuBoundAnnexBodyMemberClassification> Members { get; }
    public string IdentitySha256 { get; }

    private static string IdentityOf(EuBoundAnnexBodyClassification classification)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-bound-annex-body-classification/1");
        Append(hash, classification.Binding.IdentitySha256);
        Append(hash, classification.OfficialAddress.ArtifactRef.Sha256);
        Append(hash, classification.PdfReceipt.Reference.ContentSha256);
        Append(hash, classification.ProfileRef.ResourceId);
        Append(hash, classification.ProfileRef.Sha256);
        foreach (var member in classification.Members)
        {
            Append(hash, member.Evidence.Formex.PackageEntry);
            Append(hash, member.Evidence.PublisherAnnexId);
            Append(hash, ((int?)member.Outcome)?.ToString(CultureInfo.InvariantCulture) ?? "");
            Append(hash, ((int)member.Gap).ToString(CultureInfo.InvariantCulture));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class EuBoundAnnexBodyClassificationResult
{
    private EuBoundAnnexBodyClassificationResult(
        EuBoundAnnexBodyClassification? classification,
        EuBoundAnnexBodyClassificationRefusal refusal,
        string? detail)
    {
        Classification = classification;
        Refusal = refusal;
        Detail = detail;
    }

    public EuBoundAnnexBodyClassification? Classification { get; }
    public EuBoundAnnexBodyClassificationRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Classified => Refusal == EuBoundAnnexBodyClassificationRefusal.None;

    internal static EuBoundAnnexBodyClassificationResult Success(
        EuBoundAnnexBodyClassification classification) => new(classification,
            EuBoundAnnexBodyClassificationRefusal.None, null);

    internal static EuBoundAnnexBodyClassificationResult Refused(
        EuBoundAnnexBodyClassificationRefusal refusal, string detail) => new(null, refusal, detail);
}

/// <summary>
/// Classifies only the immutable PDF page mappings carried by a complete annex evidence binding.
/// No annex coordinate or page selection enters this public door.
/// </summary>
/// <remarks>
/// A non-null <see cref="EuDerivedPdfPageMapping"/> always contains at least one page: its
/// constructor is internal and the binder creates it only after deriving a positive Formex extent.
/// </remarks>
public sealed class EuBoundAnnexBodyClassifier
{
    private const string ProfileHeader = "lex-v3-eu-bound-annex-body-classification-profile/1";
    private const string Rule = "classification=pdfpig-0.1.11:no_glyphs+image";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ICustodyStore _custodyStore;

    public EuBoundAnnexBodyClassifier(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public async Task<EuBoundAnnexBodyClassificationResult> RunAsync(
        EuAnnexEvidenceBinding binding,
        EuDocumentFetchAddress officialAddress,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence sourceEvidence,
        ReadOnlyMemory<byte> profileBytes,
        SourceArtifactRef profileRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(officialAddress);
        ArgumentNullException.ThrowIfNull(officialRequest);
        ArgumentNullException.ThrowIfNull(terminalRequest);
        ArgumentNullException.ThrowIfNull(sourceEvidence);
        ArgumentNullException.ThrowIfNull(profileRef);
        cancellationToken.ThrowIfCancellationRequested();

        var profileDigest = Convert.ToHexStringLower(SHA256.HashData(profileBytes.Span));
        if (!string.Equals(profileDigest, profileRef.Sha256, StringComparison.Ordinal))
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.ProfileDigestMismatch,
                "the profile bytes do not carry the digest their reference names");
        }
        if (!TryReadProfile(profileBytes.Span, out var bindingIdentity, out var pdfDigest,
                out var profileFailure))
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.ProfileInvalid, profileFailure!);
        }
        if (!string.Equals(bindingIdentity, binding.IdentitySha256, StringComparison.Ordinal) ||
            !string.Equals(pdfDigest, binding.PdfReceipt.Reference.ContentSha256,
                StringComparison.Ordinal))
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.ProfileEvidenceMismatch,
                "the profile does not name this binding and its retained PDF bytes");
        }

        RepresentationChainObservation observation;
        try
        {
            observation = RepresentationChainObservation.FromRoute(sourceEvidence, terminalRequest);
        }
        catch (ArgumentException exception)
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.SourceEvidenceMismatch,
                exception.Message);
        }
        if (!MatchesSource(binding, officialAddress, officialRequest, terminalRequest,
                sourceEvidence, observation))
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.SourceEvidenceMismatch,
                "the official address, observed route and bound PDF receipt are not one source");
        }

        if (binding.Members.All(static member => member.PdfMapping is null))
        {
            var unresolved = binding.Members.Select(static member =>
                new EuBoundAnnexBodyMemberClassification(member, null,
                    EuBoundAnnexBodyClassificationGap.MappingUnresolved)).ToArray();
            return EuBoundAnnexBodyClassificationResult.Success(
                new EuBoundAnnexBodyClassification(binding, officialAddress, observation,
                    profileRef, unresolved));
        }

        ReadOnlyMemory<byte> pdfBytes;
        try
        {
            pdfBytes = await CustodyRestore.ReadCheckedAsync(
                _custodyStore, binding.PdfReceipt.Reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CustodyRequiredException
            or CustodyIntegrityException or CustodyPolicyException)
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.RetainedPdfUnavailable,
                exception.Message);
        }

        try
        {
            using var document = PdfDocument.Open(pdfBytes.ToArray());
            var members = new List<EuBoundAnnexBodyMemberClassification>(binding.Members.Count);
            foreach (var member in binding.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.PdfMapping is null)
                {
                    members.Add(new(member, null,
                        EuBoundAnnexBodyClassificationGap.MappingUnresolved));
                    continue;
                }
                if (member.PdfMapping.Pages.Any(
                        page => page.PhysicalPageNumber > document.NumberOfPages))
                {
                    members.Add(new(member, null,
                        EuBoundAnnexBodyClassificationGap.MappedPageOutsideDocument));
                    continue;
                }
                var containsText = false;
                var containsNoImage = false;
                foreach (var mappedPage in member.PdfMapping.Pages)
                {
                    var page = document.GetPage(mappedPage.PhysicalPageNumber);
                    containsText |= page.Letters.Count > 0;
                    containsNoImage |= !page.GetImages().Any();
                }
                members.Add(containsText
                    ? new(member, null, EuBoundAnnexBodyClassificationGap.BodyContainsText)
                    : containsNoImage
                        ? new(member, null, EuBoundAnnexBodyClassificationGap.BodyContainsNoImage)
                        : new(member, EuAnnexBodyDispositionOutcome.TextNotAvailable,
                            EuBoundAnnexBodyClassificationGap.None));
            }
            return EuBoundAnnexBodyClassificationResult.Success(
                new EuBoundAnnexBodyClassification(binding, officialAddress, observation,
                    profileRef, members));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Refused(EuBoundAnnexBodyClassificationRefusal.PdfUnreadable,
                exception.GetType().Name);
        }
    }

    private static bool MatchesSource(
        EuAnnexEvidenceBinding binding,
        EuDocumentFetchAddress address,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence evidence,
        RepresentationChainObservation observation)
    {
        var requestDigest = Convert.ToHexStringLower(
            SHA256.HashData(officialRequest.CopyCanonicalBytes()));
        return string.Equals(address.PsName, "cellar", StringComparison.Ordinal)
            && string.Equals(address.PsId, binding.Work.CanonicalKey, StringComparison.Ordinal)
            && address.MediaType is EuManifestationMediaType.ApplicationPdf
                or EuManifestationMediaType.PdfTypePdfa2a
            && officialRequest.Method == HttpRequestMethod.Get
            && officialRequest.Headers.Count == 2
            && terminalRequest.Headers.Count == 2
            && string.Equals(officialRequest.Uri, address.ResourceUri, StringComparison.Ordinal)
            && string.Equals(terminalRequest.Uri, observation.EffectiveUri, StringComparison.Ordinal)
            && string.Equals(requestDigest, evidence.Hops[0].LogicalRequestSha256,
                StringComparison.Ordinal)
            && string.Equals(address.ResourceUri, observation.RequestedUri, StringComparison.Ordinal)
            && HasExactHeader(officialRequest, "accept", address.Accept)
            && HasExactHeader(officialRequest, "accept-language", address.AcceptLanguage)
            && HasExactHeader(terminalRequest, "accept", address.Accept)
            && HasExactHeader(terminalRequest, "accept-language", address.AcceptLanguage)
            && observation.QualifiesAsTrustedBaselineCandidate()
            && evidence.Hops[^1].Headers.ContentType is RoutedHttpSingleHeader contentType
            && string.Equals(contentType.Value, address.Accept, StringComparison.Ordinal)
            && string.Equals(evidence.Hops[^1].DurableWriteReceiptSha256,
                DurableBlobWriteReceiptDigest.Of(binding.PdfReceipt), StringComparison.Ordinal);
    }

    private static bool HasExactHeader(HttpLogicalRequest request, string name, string value) =>
        request.Headers.Count(header => string.Equals(header.Name, name, StringComparison.Ordinal)) == 1
        && request.Headers.Any(header => string.Equals(header.Name, name, StringComparison.Ordinal)
            && string.Equals(header.Value, value, StringComparison.Ordinal));

    private static bool TryReadProfile(ReadOnlySpan<byte> bytes, out string? bindingIdentity,
        out string? pdfDigest, out string? failure)
    {
        bindingIdentity = null;
        pdfDigest = null;
        failure = null;
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            failure = "the profile is not UTF-8";
            return false;
        }
        var lines = text.Split('\n');
        if (lines.Length != 5 || lines[4].Length != 0 || lines[0] != ProfileHeader
            || !TryDigest(lines[1], "binding_identity_sha256=", out bindingIdentity)
            || !TryDigest(lines[2], "pdf_transport_sha256=", out pdfDigest)
            || lines[3] != Rule)
        {
            failure = "the profile is not the canonical bound-annex classification profile";
            return false;
        }
        return true;
    }

    private static bool TryDigest(string line, string prefix, out string? value)
    {
        value = line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;
        return value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9'
            or >= 'a' and <= 'f');
    }

    private static EuBoundAnnexBodyClassificationResult Refused(
        EuBoundAnnexBodyClassificationRefusal refusal, string detail) =>
        EuBoundAnnexBodyClassificationResult.Refused(refusal, detail);
}
