using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Lex.V3.Api;

/// <summary>
/// The live web pages, served by the API on its own origin (the owner's ruling of 2026-09-30, Decision
/// 95: one server delivers the web bundle and the API, so the pages' <c>connect-src 'self'</c> and the
/// API's absence of CORS stand). The directory is the built live pages (<c>web/dist-live</c>), placed
/// beside the API as <c>v3-web</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the files the directory holds when the API starts are served, each by its exact path, by GET
/// or HEAD; <c>/</c> is <c>index.html</c>. A path the directory does not hold is not claimed, so it
/// reaches the API's routing as before. The set is read once: a file added later is not served, and
/// nothing outside the directory can be named, because a request is matched against the recorded set,
/// never joined onto the directory.
/// </para>
/// <para>
/// Every response carries the headers this server owes a page (ruling 3): the page's own reviewed
/// Content-Security-Policy, read from <c>index.html</c> so there is one source of it, with
/// <c>frame-ancestors 'none'</c> added, which only a header can carry; <c>Strict-Transport-Security</c>;
/// <c>Referrer-Policy: no-referrer</c>; <c>X-Content-Type-Options: nosniff</c>; and
/// <c>Cache-Control: no-store</c>, so a page never outlives the corpus it was answered from.
/// </para>
/// </remarks>
internal sealed partial class V3WebRoot
{
    internal const string DirectoryName = "v3-web";

    internal const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    internal const string FrameAncestors = "frame-ancestors 'none'";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".woff2"] = "font/woff2",
    };

    private readonly IReadOnlyDictionary<string, string> _files;

    private V3WebRoot(IReadOnlyDictionary<string, string> files, string contentSecurityPolicy)
    {
        _files = files;
        ContentSecurityPolicy = contentSecurityPolicy;
    }

    /// <summary>The Content-Security-Policy header every response carries: the page's own, and frame-ancestors.</summary>
    internal string ContentSecurityPolicy { get; }

    /// <summary>The paths served, as a request names them.</summary>
    internal IReadOnlyCollection<string> Paths => (IReadOnlyCollection<string>)_files.Keys;

    /// <summary>
    /// The pages in <paramref name="directory"/>, or null when there is none to serve (no directory, or no
    /// <c>index.html</c>). A directory whose <c>index.html</c> carries no Content-Security-Policy, or whose
    /// files include one of a type this server does not serve, is refused: a page is never served without
    /// its reviewed policy, and a file is never served as a guessed type.
    /// </summary>
    internal static V3WebRoot? Open(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var index = Path.Combine(directory, "index.html");
        if (!File.Exists(index))
        {
            return null;
        }

        var root = Path.GetFullPath(directory);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!SafePath().IsMatch(relative))
            {
                throw new InvalidDataException($"The web directory holds '{relative}', a path this server does not serve.");
            }

            if (!ContentTypes.ContainsKey(Path.GetExtension(relative)))
            {
                throw new InvalidDataException($"The web directory holds '{relative}', of a type this server does not serve.");
            }

            files["/" + relative] = file;
        }

        files["/"] = files["/index.html"];
        var policy = PolicyMeta().Match(File.ReadAllText(index));
        if (!policy.Success || string.IsNullOrWhiteSpace(policy.Groups["policy"].Value))
        {
            throw new InvalidDataException("The web directory's index.html carries no Content-Security-Policy, so no page is served.");
        }

        // The attribute is HTML: the page's renderer writes each quote of 'self' as an entity.
        return new V3WebRoot(files, $"{System.Net.WebUtility.HtmlDecode(policy.Groups["policy"].Value)}; {FrameAncestors}");
    }

    /// <summary>
    /// Serves the file <paramref name="rawTarget"/> names, if this root holds it, and returns true; returns
    /// false, touching nothing, for a path it does not hold. The query string is not part of the path.
    /// </summary>
    internal async Task<bool> TryServeAsync(HttpContext context, string rawTarget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var queryAt = rawTarget.IndexOf('?', StringComparison.Ordinal);
        var path = queryAt < 0 ? rawTarget : rawTarget[..queryAt];
        if (!_files.TryGetValue(path, out var file))
        {
            return false;
        }

        var response = context.Response;
        var headers = response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.StrictTransportSecurity = StrictTransportSecurity;
        headers["Referrer-Policy"] = "no-referrer";
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "no-store";
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            headers.Allow = "GET, HEAD";
            return true;
        }

        var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ContentTypes[Path.GetExtension(file)];
        response.ContentLength = bytes.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    [GeneratedRegex(@"^[a-z0-9-]+(/[a-z0-9-]+)*\.[a-z0-9]+$")]
    private static partial Regex SafePath();

    [GeneratedRegex("<meta http-equiv=\"Content-Security-Policy\" content=\"(?<policy>[^\"]*)\"")]
    private static partial Regex PolicyMeta();
}
