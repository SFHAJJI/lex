using System.Text.Json.Serialization;

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
