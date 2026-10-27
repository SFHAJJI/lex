namespace Lex.V3.Api;

/// <summary>
/// The published key set (the specification of record's R116; the plan's item 10.5): the keys Lex's releases are signed with,
/// each with its identity, role, validity and status, at a stable address, <see cref="RawTarget"/>. The document is
/// <c>src/Lex.V3.Api/well-known/lex-keys.json</c> in the public repository, built into the server's assembly, so the image
/// serves exactly the committed bytes; its key is <c>well-known/lex-v3-release.pem</c> beside it. Only public keys are in it:
/// the private key stays in Key Vault.
/// </summary>
internal static class V3KeySet
{
    /// <summary>Where the key set is served.</summary>
    internal const string RawTarget = "/.well-known/lex-keys.json";

    private const string ResourceName = "Lex.V3.Api.WellKnown.LexKeys.json";

    /// <summary>The key set's bytes, as committed.</summary>
    internal static byte[] Document { get; } = Read();

    private static byte[] Read()
    {
        using var stream = typeof(V3KeySet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The key set {ResourceName} is not built into the server.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Answers <see cref="RawTarget"/> (a query string included, which changes nothing) and returns true; false, touching
    /// nothing, for any other target. GET and HEAD only; every other method answers 405 naming the two.
    /// </summary>
    internal static async Task<bool> TryServeAsync(HttpContext context, string rawTarget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rawTarget);
        if (!string.Equals(rawTarget, RawTarget, StringComparison.Ordinal) && !rawTarget.StartsWith(RawTarget + "?", StringComparison.Ordinal))
        {
            return false;
        }

        var response = context.Response;
        var method = context.Request.Method;
        response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = "GET, HEAD";
            response.ContentLength = 0;
            return true;
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength = Document.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(Document, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
