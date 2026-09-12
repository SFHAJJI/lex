using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Derivation;

/// <summary>The closed outcomes of one reviewed EU annex body profile.</summary>
public enum EuAnnexBodyDispositionOutcome
{
    [JsonStringEnumMemberName("admitted")]
    Admitted = 1,

    [JsonStringEnumMemberName("rejected")]
    Rejected = 2,

    [JsonStringEnumMemberName("text_not_available")]
    TextNotAvailable = 3,
}

/// <summary>
/// Retains the evidence behind one EU annex body decision without carrying invented wording.
/// </summary>
/// <remarks>
/// This type verifies a profile's already-typed outcome. It does not inspect PDF contents and does
/// not let a PDF media type stand in for evidence that a file is image-only. The profile named by
/// <see cref="ProfileRef"/> remains responsible for producing that outcome from retained bytes.
/// </remarks>
public sealed record EuAnnexBodyDisposition
{
    private EuAnnexBodyDisposition(
        SourceObjectRef sourceObject,
        EuStructuralLocation annexLocation,
        EuDocumentFetchAddress officialAddress,
        RepresentationChainObservation sourceObservation,
        DurableBlobWriteReceipt retainedTransportBytes,
        SourceArtifactRef profileRef,
        EuAnnexBodyDispositionOutcome outcome,
        string reasonCode)
    {
        SourceObject = sourceObject;
        AnnexLocation = annexLocation;
        OfficialAddress = officialAddress;
        SourceObservation = sourceObservation;
        RetainedTransportBytes = retainedTransportBytes;
        ProfileRef = profileRef;
        Outcome = outcome;
        ReasonCode = reasonCode;
        IdentitySha256 = ComputeIdentitySha256(
            sourceObject,
            annexLocation,
            officialAddress,
            retainedTransportBytes,
            profileRef,
            outcome);
    }

    public SourceObjectRef SourceObject { get; }

    public EuStructuralLocation AnnexLocation { get; }

    public EuDocumentFetchAddress OfficialAddress { get; }

    public RepresentationChainObservation SourceObservation { get; }

    public DurableBlobWriteReceipt RetainedTransportBytes { get; }

    public SourceArtifactRef ProfileRef { get; }

    public EuAnnexBodyDispositionOutcome Outcome { get; }

    public string ReasonCode { get; }

    public string TransportByteSha256 => RetainedTransportBytes.Reference.ContentSha256;

    /// <summary>
    /// A byte-stable identity over the legal identity, annex location, official address, retained
    /// bytes, profile and outcome. Run timestamps and observation ids are lineage, not identity.
    /// </summary>
    public string IdentitySha256 { get; }

    /// <summary>
    /// Verifies that identity, annex location, request address, observed route and retained bytes
    /// describe one Cellar PDF annex before retaining the profile's closed outcome.
    /// </summary>
    public static EuAnnexBodyDisposition Create(
        SourceObjectRef sourceObject,
        EuStructuralLocation annexLocation,
        EuDocumentFetchAddress officialAddress,
        HttpLogicalRequest logicalRequest,
        RoutedHttpEvidence sourceEvidence,
        DurableBlobWriteReceipt retainedTransportBytes,
        SourceArtifactRef profileRef,
        EuAnnexBodyDispositionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(annexLocation);
        ArgumentNullException.ThrowIfNull(officialAddress);
        ArgumentNullException.ThrowIfNull(logicalRequest);
        ArgumentNullException.ThrowIfNull(sourceEvidence);
        ArgumentNullException.ThrowIfNull(retainedTransportBytes);
        ArgumentNullException.ThrowIfNull(profileRef);

        var sourceObservation = RepresentationChainObservation.FromRoute(
            sourceEvidence,
            logicalRequest);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (sourceObject.Authority != SourceAuthority.Cellar)
        {
            throw new ArgumentException("An EU annex must retain a Cellar source identity.", nameof(sourceObject));
        }

        if (!annexLocation.Tokens.Any(static token =>
                string.Equals(token.Code, "AN", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The structural location carries no publisher annex token.",
                nameof(annexLocation));
        }

        if (!string.Equals(officialAddress.PsName, "cellar", StringComparison.Ordinal) ||
            !string.Equals(officialAddress.PsId, sourceObject.CanonicalKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The official address does not identify the retained Cellar source object.",
                nameof(officialAddress));
        }

        if (!string.Equals(officialAddress.ResourceUri, logicalRequest.Uri, StringComparison.Ordinal) ||
            !string.Equals(officialAddress.ResourceUri, sourceObservation.RequestedUri, StringComparison.Ordinal) ||
            !HasExactHeader(logicalRequest, "accept", officialAddress.Accept) ||
            !HasExactHeader(logicalRequest, "accept-language", officialAddress.AcceptLanguage))
        {
            throw new ArgumentException(
                "The official address is not the exact URI and header set the observed route requested.",
                nameof(logicalRequest));
        }

        if (!sourceObservation.QualifiesAsTrustedBaselineCandidate())
        {
            throw new ArgumentException(
                "The source observation is not a complete derivable body transfer.",
                nameof(sourceObservation));
        }

        if (sourceObservation.ReceivedEntityByteCount !=
                checked((ulong)retainedTransportBytes.Reference.ByteLength) ||
            !string.Equals(
                sourceObservation.TransportByteSha256,
                retainedTransportBytes.Reference.ContentSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                sourceEvidence.Hops[^1].DurableWriteReceiptSha256,
                DurableBlobWriteReceiptDigest.Of(retainedTransportBytes),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The retained receipt is not the exact receipt bound to the source observation.",
                nameof(retainedTransportBytes));
        }

        if (outcome == EuAnnexBodyDispositionOutcome.TextNotAvailable &&
            officialAddress.MediaType is not EuManifestationMediaType.ApplicationPdf
                and not EuManifestationMediaType.PdfTypePdfa2a)
        {
            throw new ArgumentException(
                "The image-only annex gap is admitted only for an official PDF address.",
                nameof(officialAddress));
        }

        var reasonCode = outcome switch
        {
            EuAnnexBodyDispositionOutcome.Admitted => "body_admitted",
            EuAnnexBodyDispositionOutcome.Rejected => "profile_rejected",
            EuAnnexBodyDispositionOutcome.TextNotAvailable => "image_only",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        return new EuAnnexBodyDisposition(
            sourceObject,
            annexLocation,
            officialAddress,
            sourceObservation,
            retainedTransportBytes,
            profileRef,
            outcome,
            reasonCode);
    }

    private static bool HasExactHeader(HttpLogicalRequest request, string name, string value) =>
        request.Headers.Count(header => string.Equals(header.Name, name, StringComparison.Ordinal)) == 1 &&
        request.Headers.Any(header =>
            string.Equals(header.Name, name, StringComparison.Ordinal) &&
            string.Equals(header.Value, value, StringComparison.Ordinal));

    private static string ComputeIdentitySha256(
        SourceObjectRef sourceObject,
        EuStructuralLocation annexLocation,
        EuDocumentFetchAddress officialAddress,
        DurableBlobWriteReceipt retainedTransportBytes,
        SourceArtifactRef profileRef,
        EuAnnexBodyDispositionOutcome outcome)
    {
        var canonical = string.Join('\n',
            "eu-annex-body-disposition/1",
            "source=" + sourceObject.CanonicalKeySha256,
            "annex=" + annexLocation.RawValue,
            "address=" + officialAddress.ArtifactRef.Sha256,
            "bytes=" + retainedTransportBytes.Reference.ContentSha256,
            "profile=" + profileRef.Sha256,
            "outcome=" + ((int)outcome).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
