using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The closed set of Cellar REST dissemination manifestation media types this route admits, per
/// the PROVEN grounding in <c>review/23-research-temporal.md</c> section 1.2. Each member is one
/// exact <c>Accept</c> header value; there is no wildcard or preference-list member, matching
/// SCOPE_RULING lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef item 2: "one GET per
/// accepted object with exactly one Accept ... never a preference list."
/// </summary>
public enum EuManifestationMediaType
{
    [JsonStringEnumMemberName("xhtml_xml")]
    XhtmlXml = 1,

    [JsonStringEnumMemberName("zip_mtype_fmx4")]
    ZipMtypeFmx4 = 2,

    [JsonStringEnumMemberName("pdf_type_pdfa2a")]
    PdfTypePdfa2a = 3,

    [JsonStringEnumMemberName("rdf_xml")]
    RdfXml = 4,

    [JsonStringEnumMemberName("rdf_xml_notice_tree")]
    RdfXmlNoticeTree = 5,

    [JsonStringEnumMemberName("xml_notice_branch")]
    XmlNoticeBranch = 6,

    [JsonStringEnumMemberName("xml_notice_object")]
    XmlNoticeObject = 7,

    [JsonStringEnumMemberName("xml_notice_identifier")]
    XmlNoticeIdentifier = 8,
}

/// <summary>
/// The closed set of <c>Accept-Language</c> ISO 639-3 codes this route admits: the two PROVEN
/// observed in the research (<c>eng</c>, <c>fra</c>). Widening this set is a new frozen-order-of-
/// evidence claim, the same discipline <see cref="EuWatermarkWitnessPlan.AdmittedShapes"/>
/// already applies to lexical shapes: only what has actually been observed is admitted.
/// </summary>
public enum EuDocumentLanguage
{
    [JsonStringEnumMemberName("eng")]
    Eng = 1,

    [JsonStringEnumMemberName("fra")]
    Fra = 2,
}

/// <summary>
/// Why <see cref="EuDocumentFetchAddress.TryCreate"/> refused to mint an address. Closed.
/// </summary>
public enum EuDocumentFetchAddressRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("ps_name_shape_invalid")]
    PsNameShapeInvalid = 1,

    [JsonStringEnumMemberName("ps_id_shape_invalid")]
    PsIdShapeInvalid = 2,
}

/// <summary>
/// The typed, fully-formed EU Cellar document-fetch address: SCOPE_RULING
/// lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef item 2. Carries the admitted
/// host, the resource path, the exact <c>Accept</c> media type and the <c>Accept-Language</c> code
/// this route was minted for. Once constructed this value IS the complete address: rendering it to
/// a request reads its fields verbatim, with no string building at fetch time.
/// </summary>
/// <remarks>
/// <para>
/// Grounding (PROVEN, <c>review/23-research-temporal.md</c> section 1.2, independently reconfirmed
/// live on 2026-09-04): the URL pattern is
/// <c>https://publications.europa.eu/resource/{ps-name}/{ps-id}</c>, for example
/// <c>ps-name=celex, ps-id=32016R0679</c>. <c>ps-name=cellar</c> paired with a Cellar work/
/// expression/manifestation key is the same pattern; <see cref="EuWemiIdentityBoundary"/>'s own
/// <c>CellarOrigins</c> constant already proves every Cellar WEMI object's own
/// <c>PublisherUri</c> is exactly <c>https://publications.europa.eu/resource/cellar/{key}</c>
/// (or the <c>http://</c> scheme), so this type's <c>ps-id</c> is that same <c>CanonicalKey</c>.
/// </para>
/// <para>
/// Distinct from that identity IRI, per the ruling's own words from the earlier split ruling: the
/// object's identity is carried at <c>http://...</c>; this fetch address always renders to
/// <c>https://...</c>, because <see cref="RoutedHttpValidation.RequireAbsoluteHttpsUri"/> admits no
/// other scheme.
/// </para>
/// </remarks>
public sealed class EuDocumentFetchAddress
{
    /// <summary>
    /// The one admitted host: the same <c>publications.europa.eu</c> this route's robots policy and
    /// the EU SPARQL query channel already trust.
    /// </summary>
    public const string AdmittedHost = "publications.europa.eu";

    private const string ResourceCollectionRoot = "https://" + AdmittedHost + "/resource/";
    private const int MaximumPsNameLength = 64;
    private const int MaximumPsIdLength = 256;
    private const string CanonicalizationIdentity = "eu-document-fetch-address/1";

    private readonly byte[] _canonicalIdentityBytes;

    private EuDocumentFetchAddress(
        string psName,
        string psId,
        EuManifestationMediaType mediaType,
        EuDocumentLanguage language,
        string resourceUri,
        string accept,
        string acceptLanguage,
        byte[] canonicalIdentityBytes)
    {
        PsName = psName;
        PsId = psId;
        MediaType = mediaType;
        Language = language;
        ResourceUri = resourceUri;
        Accept = accept;
        AcceptLanguage = acceptLanguage;
        _canonicalIdentityBytes = canonicalIdentityBytes;
        ArtifactRef = new SourceArtifactRef(
            "urn:uuid:7a6c3d2e-4b1f-4a8e-9c3d-2f6b1e8a4d0c",
            Convert.ToHexString(SHA256.HashData(canonicalIdentityBytes)).ToLowerInvariant());
    }

    /// <summary>The production system name, e.g. <c>celex</c> or <c>cellar</c>.</summary>
    public string PsName { get; }

    /// <summary>The production-system-scoped identifier, e.g. a CELEX number or a Cellar key.</summary>
    public string PsId { get; }

    public EuManifestationMediaType MediaType { get; }

    public EuDocumentLanguage Language { get; }

    /// <summary>
    /// The complete, exact <c>https://publications.europa.eu/resource/{ps-name}/{ps-id}</c> target.
    /// This is the real wire request-target; nothing downstream builds it from parts again.
    /// </summary>
    public string ResourceUri { get; }

    /// <summary>The exact <c>Accept</c> header value this address was minted for.</summary>
    public string Accept { get; }

    /// <summary>The exact <c>Accept-Language</c> header value this address was minted for.</summary>
    public string AcceptLanguage { get; }

    /// <summary>
    /// This address's own content-addressed identity, for use as an
    /// <c>IMachineQueryRenderer.RendererProfileRef</c> the same way
    /// <see cref="EuWatermarkWitnessPlan.ArtifactRef"/> already serves that role for the
    /// witness family.
    /// </summary>
    public SourceArtifactRef ArtifactRef { get; }

    public byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    /// <summary>
    /// The only path that mints an address.
    /// </summary>
    public static EuDocumentFetchAddress? TryCreate(
        string psName,
        string psId,
        EuManifestationMediaType mediaType,
        EuDocumentLanguage language,
        out EuDocumentFetchAddressRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(psName);
        ArgumentNullException.ThrowIfNull(psId);

        if (!IsAdmittedPsNameShape(psName))
        {
            refusal = EuDocumentFetchAddressRefusal.PsNameShapeInvalid;
            return null;
        }

        if (!IsAdmittedPsIdShape(psId))
        {
            refusal = EuDocumentFetchAddressRefusal.PsIdShapeInvalid;
            return null;
        }

        var resourceUri = ResourceCollectionRoot + psName + "/" + psId;
        // The renderer never builds this string again: RequireAbsoluteHttpsUri here is this type's
        // own proof that the address it is about to freeze is real, sendable wire text, not a
        // second, independent construction of it at fetch time.
        resourceUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(resourceUri, nameof(resourceUri));

        var accept = AcceptToken(mediaType);
        var acceptLanguage = LanguageToken(language);
        var identityBytes = BuildCanonicalIdentityBytes(resourceUri, accept, acceptLanguage);
        refusal = EuDocumentFetchAddressRefusal.None;
        return new EuDocumentFetchAddress(
            psName,
            psId,
            mediaType,
            language,
            resourceUri,
            accept,
            acceptLanguage,
            identityBytes);
    }

    /// <summary>
    /// Structural admission for <c>OfficialMachineQuerySourceProfiles.ResolveFor</c>'s widened
    /// switch: does this exact requested URI have the shape this route admits (the resource
    /// collection root, followed by exactly two nonempty path segments, no query, no fragment)?
    /// This checks shape only; it cannot recover the <see cref="Accept"/> or
    /// <see cref="AcceptLanguage"/> a real address was minted with, because those never appear in
    /// the URI at all -- they are request headers, carried instead on the bound machine-query
    /// input's own parameters (see <c>EuDocumentFetchRenderer</c>).
    /// </summary>
    public static bool IsAdmittedResourceUri(string requestedUri)
    {
        ArgumentNullException.ThrowIfNull(requestedUri);
        if (!requestedUri.StartsWith(ResourceCollectionRoot, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = requestedUri[ResourceCollectionRoot.Length..];
        var segments = remainder.Split('/');
        if (segments.Length != 2)
        {
            return false;
        }

        if (!IsAdmittedPsNameShape(segments[0]) || !IsAdmittedPsIdShape(segments[1]))
        {
            return false;
        }

        try
        {
            _ = RoutedHttpValidation.RequireAbsoluteHttpsUri(requestedUri, nameof(requestedUri));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts this publisher-specific typed value into the publisher-neutral projection
    /// <see cref="ScopeManifest.ScopeManifestRow.FetchAddress"/> carries. The conversion is a plain
    /// field copy; no new validation or derivation happens here.
    /// </summary>
    public ScopeManifestFetchAddress ToManifestFetchAddress() =>
        ScopeManifestFetchAddress.Minted(AdmittedHost, PsName + "/" + PsId, Accept, AcceptLanguage);

    private static bool IsAdmittedPsNameShape(string value) =>
        value.Length is > 0 and <= MaximumPsNameLength &&
        value.All(static character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static bool IsAdmittedPsIdShape(string value) =>
        value.Length is > 0 and <= MaximumPsIdLength &&
        value.All(static character =>
            character is > ' ' and <= '~' and not '/' and not '?' and not '#');

    private static string AcceptToken(EuManifestationMediaType mediaType) => mediaType switch
    {
        EuManifestationMediaType.XhtmlXml => "application/xhtml+xml",
        EuManifestationMediaType.ZipMtypeFmx4 => "application/zip;mtype=fmx4",
        EuManifestationMediaType.PdfTypePdfa2a => "application/pdf;type=pdfa2a",
        EuManifestationMediaType.RdfXml => "application/rdf+xml",
        EuManifestationMediaType.RdfXmlNoticeTree => "application/rdf+xml;notice=tree",
        EuManifestationMediaType.XmlNoticeBranch => "application/xml;notice=branch",
        EuManifestationMediaType.XmlNoticeObject => "application/xml;notice=object",
        EuManifestationMediaType.XmlNoticeIdentifier => "application/xml;notice=identifier",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    private static string LanguageToken(EuDocumentLanguage language) => language switch
    {
        EuDocumentLanguage.Eng => "eng",
        EuDocumentLanguage.Fra => "fra",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    private static byte[] BuildCanonicalIdentityBytes(
        string resourceUri, string accept, string acceptLanguage) =>
        Encoding.UTF8.GetBytes(string.Join(
            '\n',
            CanonicalizationIdentity,
            "resource_uri=" + resourceUri,
            "accept=" + accept,
            "accept_language=" + acceptLanguage));
}
