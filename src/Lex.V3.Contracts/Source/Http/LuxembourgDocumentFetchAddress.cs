using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// D1-06c-LU item 2: closed, named reasons a candidate Legilux store file URI is refused. Mirrors
/// v2's exact <c>LegiluxAdapter.OfficialManifestationTransport</c> validation (Decision 22; see
/// C:/lex, src/Lex.Sources.Legilux/LegiluxAdapter.cs), reproduced here rather than referenced
/// because Source/Scope and Ingest/Luxembourg are out of this lane's path.
/// </summary>
public enum LuxembourgFileUriRefusalReason
{
    [JsonStringEnumMemberName("not_absolute_uri")]
    NotAbsoluteUri = 1,

    [JsonStringEnumMemberName("unsupported_scheme")]
    UnsupportedScheme = 2,

    [JsonStringEnumMemberName("unexpected_host")]
    UnexpectedHost = 3,

    [JsonStringEnumMemberName("non_default_port")]
    NonDefaultPort = 4,

    [JsonStringEnumMemberName("user_info_present")]
    UserInfoPresent = 5,

    [JsonStringEnumMemberName("query_present")]
    QueryPresent = 6,

    [JsonStringEnumMemberName("fragment_present")]
    FragmentPresent = 7,

    [JsonStringEnumMemberName("path_not_under_filestore")]
    PathNotUnderFilestore = 8,
}

/// <summary>
/// A typed refusal that names the exact rejected URI in its message, per the scope ruling's item
/// 2 ("anything else a typed refusal naming the URI"): never a silent drop, never a generic
/// wrapper that loses the input.
/// </summary>
public sealed class LuxembourgFileUriRefusedException : ArgumentException
{
    internal LuxembourgFileUriRefusedException(
        string rejectedUri,
        LuxembourgFileUriRefusalReason reason,
        string message)
        : base(message)
    {
        RejectedUri = rejectedUri;
        Reason = reason;
    }

    public string RejectedUri { get; }

    public LuxembourgFileUriRefusalReason Reason { get; }
}

/// <summary>
/// D1-06c-LU items 2 and 3. One validated Legilux data-host file URI, the exact shape the
/// publisher's own SPARQL store names via <c>jolux:isExemplifiedBy</c> on a manifestation node:
/// scheme http or https, host <c>data.legilux.public.lu</c>, default port for that scheme, no
/// userinfo, no query, no fragment, path strictly under <c>/filestore/</c>. This is the input-side
/// validation and the host mapping only; minting this value FROM a real
/// <c>ScopeManifestRow</c>/SPARQL result and wiring it into the LU adapter are out of this lane's
/// path (D1-06c-EU's schema bump owns <c>Source/Scope</c>).
/// </summary>
public sealed class LuxembourgFileUri
{
    private const string ExpectedHost = "data.legilux.public.lu";
    private const string FilestorePrefix = "/filestore/";
    private const string FetchHost = "legilux.public.lu";

    private LuxembourgFileUri(Uri value) => Value = value;

    /// <summary>The validated <c>data.legilux.public.lu</c> store URI, unchanged.</summary>
    public Uri Value { get; }

    /// <summary>
    /// Item 2's validator. Accepts ONLY what v2's <c>OfficialManifestationTransport</c> accepts;
    /// anything else is a typed <see cref="LuxembourgFileUriRefusedException"/> naming the exact
    /// rejected URI, never a silent drop.
    /// </summary>
    public static LuxembourgFileUri RequireValid(string candidateUri)
    {
        ArgumentNullException.ThrowIfNull(candidateUri);

        if (!Uri.TryCreate(candidateUri, UriKind.Absolute, out var uri))
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.NotAbsoluteUri,
                $"The Legilux file URI '{candidateUri}' is not an absolute URI.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.UnsupportedScheme,
                $"The Legilux file URI '{candidateUri}' has an unsupported scheme.");
        }

        if (!string.Equals(uri.Host, ExpectedHost, StringComparison.Ordinal))
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.UnexpectedHost,
                $"The Legilux file URI '{candidateUri}' is not hosted on {ExpectedHost}.");
        }

        if (!uri.IsDefaultPort)
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.NonDefaultPort,
                $"The Legilux file URI '{candidateUri}' does not use its scheme's default port.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.UserInfoPresent,
                $"The Legilux file URI '{candidateUri}' carries user info.");
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.QueryPresent,
                $"The Legilux file URI '{candidateUri}' carries a query string.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.FragmentPresent,
                $"The Legilux file URI '{candidateUri}' carries a fragment.");
        }

        if (!uri.AbsolutePath.StartsWith(FilestorePrefix, StringComparison.Ordinal) ||
            uri.AbsolutePath.Length == FilestorePrefix.Length)
        {
            throw new LuxembourgFileUriRefusedException(
                candidateUri,
                LuxembourgFileUriRefusalReason.PathNotUnderFilestore,
                $"The Legilux file URI '{candidateUri}' is not strictly longer than {FilestorePrefix}.");
        }

        return new LuxembourgFileUri(uri);
    }

    /// <summary>
    /// Item 3: the exact fetch address on the robots-permitted www host. Same path, host changed
    /// (the "data." subdomain dropped), scheme normalized to https regardless of the validated
    /// URI's own scheme, matching v2's own fetch behaviour exactly (v2 always fetches https
    /// against legilux.public.lu regardless of what the store's URI said).
    /// </summary>
    public Uri ToFetchUri() => new UriBuilder(Value)
    {
        Scheme = Uri.UriSchemeHttps,
        Host = FetchHost,
        Port = -1,
    }.Uri;
}

/// <summary>
/// The publisher's own authority IRIs for the two selection facts, parsed by exact prefix and last
/// segment rather than by substring. The correction this shape exists to prevent is recorded in
/// PROBE_RESULT lex-event-20260904T174227089Z-8f2c03f33d1c4e95b397323c992bbfce: a first pass
/// classified formats by substring against whole IRIs and produced a frequency table that
/// contradicted itself, because "xml" is a substring of "xml-akomantoso" and "pdf" of "pdfa".
/// </summary>
public static class LuxembourgAuthorityIri
{
    private const string UserFormatPrefix =
        "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string LegalValuePrefix =
        "http://data.legilux.public.lu/resource/authority/statut-version/";

    /// <summary>
    /// The exact userFormat token, or null for any other authority value. Returns null rather than
    /// throwing for the store's real non-wording tokens (html, doc, docx, svg), which this route
    /// never selects; a caller drops such a candidate instead of routing it.
    /// </summary>
    public static LuxembourgUserFormatToken? TryParseUserFormat(string iri)
    {
        ArgumentNullException.ThrowIfNull(iri);
        return LastSegment(iri, UserFormatPrefix) switch
        {
            "xml-akomantoso" => LuxembourgUserFormatToken.XmlAkomaNtoso,
            "xml" => LuxembourgUserFormatToken.Xml,
            "pdfa" => LuxembourgUserFormatToken.PdfA,
            "pdf" => LuxembourgUserFormatToken.Pdf,
            _ => null,
        };
    }

    /// <summary>
    /// The exact legalValue marker, or null for any other authority value. Store wide there are
    /// exactly two, so null here means the manifestation carries no readable marker at all, which
    /// the caller must decide about rather than default.
    /// </summary>
    public static LuxembourgLegalValue? TryParseLegalValue(string iri)
    {
        ArgumentNullException.ThrowIfNull(iri);
        return LastSegment(iri, LegalValuePrefix) switch
        {
            "officiel" => LuxembourgLegalValue.Officiel,
            "definitif" => LuxembourgLegalValue.Definitif,
            _ => null,
        };
    }

    private static string? LastSegment(string iri, string prefix)
    {
        if (!iri.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var segment = iri[prefix.Length..];
        return segment.Length == 0 || segment.Contains('/') ? null : segment;
    }
}

/// <summary>
/// D1-06c-LU-2 item 1: one Luxembourg document-fetch address, minted from the store's own
/// <c>jolux:isExemplifiedBy</c> file URI. It carries what the route actually needs and nothing
/// else: the validated store URI, the www-host fetch URI it maps to, the EXACT userFormat token
/// held (never a normalised category), the publisher legal-value marker that decided the selection,
/// and the act's own ELI page path.
/// </summary>
/// <remarks>
/// The act ELI page path is here, rather than being derived at send time, because it cannot be
/// derived at all from the filestore path. RULING
/// lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143 makes robots evaluate three paths
/// for every manifestation, the fetch path, the page path derived from the filestore path, and the
/// act's own ELI page path from the store's manifestation to expression to work relation, and the
/// third is what catches a manifestation whose filestore path lands outside its own act. The two
/// live examples that forced it: the loi 2007/01/15/n2 PDF, whose robots line names
/// <c>/eli/etat/leg/memorial/2007/8/fr/pdf</c> while the file lives under
/// <c>memorial/2007/a8</c> so a literal match misses it, and the rgd 1977/11/16/n3 PDF, whose file
/// lives under <c>memorial/1977/a67</c> rather than under the act's own ELI path at all.
/// </remarks>
public sealed class LuxembourgDocumentFetchAddress
{
    private const string CanonicalizationIdentity = "lu-document-fetch-address/1";
    private const int MaximumPathLength = 512;

    private readonly byte[] _canonicalIdentityBytes;

    private LuxembourgDocumentFetchAddress(
        LuxembourgFileUri storeFileUri,
        LuxembourgUserFormatToken userFormatToken,
        LuxembourgLegalValue legalValue,
        string actEliPagePath)
    {
        StoreFileUri = storeFileUri;
        UserFormatToken = userFormatToken;
        LegalValue = legalValue;
        ActEliPagePath = actEliPagePath;
        FetchUri = storeFileUri.ToFetchUri();
        _canonicalIdentityBytes = Encoding.UTF8.GetBytes(string.Join('\n',
            $"schema={CanonicalizationIdentity}",
            $"store_file_uri={storeFileUri.Value.AbsoluteUri}",
            $"fetch_uri={FetchUri.AbsoluteUri}",
            $"user_format={UserFormatTokenName(userFormatToken)}",
            $"legal_value={LegalValueName(legalValue)}",
            $"act_eli_page_path={actEliPagePath}",
            string.Empty));
        ArtifactRef = new SourceArtifactRef(
            "urn:uuid:2f1c6d84-9b0a-4a3d-8f57-6c2e1b9d4a70",
            Convert.ToHexString(SHA256.HashData(_canonicalIdentityBytes)).ToLowerInvariant());
    }

    /// <summary>The validated <c>data.legilux.public.lu</c> store URI this address was minted from.</summary>
    public LuxembourgFileUri StoreFileUri { get; }

    /// <summary>The exact wire target on the robots-permitted www host.</summary>
    public Uri FetchUri { get; }

    /// <summary>The exact <c>jolux:userFormat</c> token held, for the corpus record to name.</summary>
    public LuxembourgUserFormatToken UserFormatToken { get; }

    /// <summary>The publisher's own <c>jolux:legalValue</c> marker for this manifestation.</summary>
    public LuxembourgLegalValue LegalValue { get; }

    /// <summary>
    /// The act's own ELI page path, the publisher's grouping key for robots purposes. Absolute
    /// path text only, for example <c>/eli/etat/leg/loi/2007/01/15/n2/jo</c>.
    /// </summary>
    public string ActEliPagePath { get; }

    /// <summary>
    /// This address's own identity, paired with real canonical bytes exactly as
    /// <c>EuDocumentFetchAddress.ArtifactRef</c> is, so a bound request's renderer profile names
    /// this address's content rather than an inert placeholder.
    /// </summary>
    public SourceArtifactRef ArtifactRef { get; }

    public byte[] CopyCanonicalIdentityBytes() => _canonicalIdentityBytes.ToArray();

    /// <summary>
    /// The only path that mints an address. The file URI is validated by
    /// <see cref="LuxembourgFileUri.RequireValid"/> before it reaches here, so this door adds only
    /// the checks that URI validation cannot make: a real act ELI page path.
    /// </summary>
    public static LuxembourgDocumentFetchAddress Create(
        LuxembourgFileUri storeFileUri,
        LuxembourgUserFormatToken userFormatToken,
        LuxembourgLegalValue legalValue,
        string actEliPagePath)
    {
        ArgumentNullException.ThrowIfNull(storeFileUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(actEliPagePath);
        if (!Enum.IsDefined(userFormatToken))
        {
            throw new ArgumentOutOfRangeException(nameof(userFormatToken));
        }

        if (!Enum.IsDefined(legalValue))
        {
            throw new ArgumentOutOfRangeException(nameof(legalValue));
        }

        if (actEliPagePath[0] != '/' ||
            actEliPagePath.Length > MaximumPathLength ||
            actEliPagePath.Contains('?', StringComparison.Ordinal) ||
            actEliPagePath.Contains('#', StringComparison.Ordinal) ||
            actEliPagePath.Any(static character => character is < '!' or > '~'))
        {
            throw new ArgumentException(
                "An act ELI page path is one bounded printable absolute path with no query or fragment.",
                nameof(actEliPagePath));
        }

        return new LuxembourgDocumentFetchAddress(
            storeFileUri, userFormatToken, legalValue, actEliPagePath);
    }

    /// <summary>
    /// This address projected onto the publisher-neutral manifest row shape. The route negotiates
    /// nothing, so it mints the non-negotiating form: host and resource path only, with no Accept
    /// pair to invent (see <see cref="ScopeManifestFetchAddress.MintedWithoutNegotiation"/>).
    /// </summary>
    public ScopeManifestFetchAddress ToScopeManifestFetchAddress() =>
        ScopeManifestFetchAddress.MintedWithoutNegotiation(FetchUri.Host, FetchUri.AbsolutePath);

    internal static string UserFormatTokenName(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
        LuxembourgUserFormatToken.Xml => "xml",
        LuxembourgUserFormatToken.PdfA => "pdfa",
        LuxembourgUserFormatToken.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    internal static string LegalValueName(LuxembourgLegalValue value) => value switch
    {
        LuxembourgLegalValue.Officiel => "officiel",
        LuxembourgLegalValue.Definitif => "definitif",
        LuxembourgLegalValue.Unstated => "unstated",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
