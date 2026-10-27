using Microsoft.AspNetCore.Http;

namespace Lex.V3.Api;

/// <summary>
/// The site's answer to a page request nothing claims (the specification of record's R60: "A 404 page offers search"; plan
/// item 6.9): the built 404 page, in the reader's language, with status 404 and every header a page carries, as the live
/// server answers it (<c>web/scripts/not-found.mjs</c>; a test holds the two to one table,
/// <c>tests/Lex.V3.Tests/Fixtures/SitePaths/not-found.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// A page request is told from a machine's by its method and its path alone, never by a header: a GET or a HEAD whose path
/// is not under <c>/api/</c>, <c>/mcp</c> or <c>/health/</c>. A machine's path keeps the platform's JSON problem
/// (<c>unknown_route</c>), as does every other method. The page is in the interface language the path stands under
/// (<c>/fr/…</c>, <c>/en/…</c>), else in the one the root's redirect chooses (<see cref="NegotiateLanguage"/>, from the one
/// header this server reads, Accept-Language, which goes into that pure choice and nowhere else), and the answer then
/// says <c>Vary: Accept-Language</c>.
/// </para>
/// <para>
/// The build writes one 404 page per interface language under <c>not-found/</c> and one maintenance page per language under
/// <c>maintenance/</c>. Neither is served at its own address: a 404 page is only ever the body of a 404, and a maintenance
/// page only the body of a maintenance revision's answer to a page request, chosen as the 404 page is
/// (<c>V3WebRoot.Maintenance.cs</c>); a server that is not one never shows it. A directory without 404 pages (a build before
/// them) leaves every path nothing claims to the API's routing, as before.
/// </para>
/// </remarks>
internal sealed partial class V3WebRoot
{
    /// <summary>Where the build writes each interface language's 404 page, <c>{language}.html</c>.</summary>
    internal const string NotFoundDirectory = "not-found";

    /// <summary>Where the build writes each interface language's maintenance page, <c>{language}.html</c>, which a maintenance revision shows.</summary>
    internal const string MaintenanceDirectory = "maintenance";

    /// <summary>The paths of machines, which keep their JSON problems: the API, the MCP endpoint and the health routes.</summary>
    internal static readonly IReadOnlyList<string> MachinePathPrefixes = ["/api/", "/mcp", "/health/"];

    /// <summary>The 404 pages this root holds, as the build names them (<c>not-found/fr.html</c>).</summary>
    internal IReadOnlyCollection<string> NotFoundPages => (IReadOnlyCollection<string>)_notFoundPages.Keys;

    /// <summary>Whether a request is a page's: a GET or a HEAD whose path, with its leading slash, is not a machine's.</summary>
    internal static bool IsPageRequest(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        return (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) &&
            path.StartsWith('/') &&
            !MachinePathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The 404 page a request nothing claims is answered with, as <c>notFoundPageFor</c> in <c>web/scripts/not-found.mjs</c>
    /// chooses it: <c>Page</c> the built file (<c>not-found/fr.html</c>), and <c>Negotiated</c> whether its language came
    /// from <paramref name="acceptLanguage"/> rather than the path. Null when it is not a page request.
    /// </summary>
    internal static (string Page, bool Negotiated)? NotFoundPageFor(string method, string path, string? acceptLanguage)
    {
        if (!IsPageRequest(method, path))
        {
            return null;
        }

        var language = InterfaceLanguages.FirstOrDefault(candidate => path.StartsWith($"/{candidate}/", StringComparison.Ordinal));
        return ($"{NotFoundDirectory}/{language ?? NegotiateLanguage(acceptLanguage)}.html", language is null);
    }

    /// <summary>
    /// Answers a page request nothing claims with the 404 page in the reader's language (<see cref="NotFoundPageFor"/>), with
    /// status 404 and the headers every page carries, and returns true; returns false, touching nothing, for any other
    /// request, or when this root holds no such page.
    /// </summary>
    internal Task<bool> TryServeNotFoundAsync(HttpContext context, string rawTarget, CancellationToken cancellationToken) =>
        TryServeBuiltPageAsync(context, rawTarget, NotFoundDirectory, _notFoundPages, StatusCodes.Status404NotFound, null, cancellationToken);

    /// <summary>
    /// Answers a page request with the build's page of <paramref name="directory"/> (one of <paramref name="pages"/>, by the
    /// build's name, <c>{directory}/{language}.html</c>) in the language <see cref="NotFoundPageFor"/> chooses, with
    /// <paramref name="status"/>, the headers every page carries and, when given, <c>Retry-After</c>
    /// (<paramref name="retryAfter"/>), and returns true; returns false, touching nothing, for any other request, or when
    /// this root holds no such page. The 404 page and the maintenance page are chosen by the one choice.
    /// </summary>
    private async Task<bool> TryServeBuiltPageAsync(
        HttpContext context,
        string rawTarget,
        string directory,
        IReadOnlyDictionary<string, string> pages,
        int status,
        string? retryAfter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rawTarget);
        var queryAt = rawTarget.IndexOf('?', StringComparison.Ordinal);
        var path = queryAt < 0 ? rawTarget : rawTarget[..queryAt];
        var method = context.Request.Method;
        // The language is the 404 page's choice, whose file is named by it, as each page of the directory is.
        if (NotFoundPageFor(method, path, context.Request.Headers.AcceptLanguage.ToString()) is not { } chosen ||
            !pages.TryGetValue($"{directory}/{Path.GetFileName(chosen.Page)}", out var file))
        {
            return false;
        }

        var response = context.Response;
        _ = WritePageHeaders(response, method);
        if (chosen.Negotiated)
        {
            // The page's language came from the reader's languages, so a cache must not hand it to another reader.
            response.Headers.Vary = "Accept-Language";
        }

        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = retryAfter;
        }

        var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        response.StatusCode = status;
        response.ContentType = ContentTypes[".html"];
        response.ContentLength = bytes.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Whether a file of the web directory is one of the build's pages no request names, recording it under its name: a 404
    /// page (<c>not-found/{language}.html</c>) in <paramref name="notFound"/>, or a maintenance page
    /// (<c>maintenance/{language}.html</c>) in <paramref name="maintenance"/>.
    /// </summary>
    private static bool IsUnservedPage(string relative, string file, Dictionary<string, string> notFound, Dictionary<string, string> maintenance)
    {
        if (relative.StartsWith(NotFoundDirectory + "/", StringComparison.Ordinal))
        {
            notFound[relative] = file;
            return true;
        }

        if (relative.StartsWith(MaintenanceDirectory + "/", StringComparison.Ordinal))
        {
            maintenance[relative] = file;
            return true;
        }

        return false;
    }
}
