namespace Lex.V3.Api;

/// <summary>
/// The security headers every answer of this host carries, not only its pages (the specification of record's shell line:
/// "It sends HSTS, frame-ancestors, Referrer-Policy and a CSP that defaults to self"; the owner's ruling 3 of 2026-09-30;
/// the plan's item 10.7: "Security headers apply to the API's answers too").
/// </summary>
/// <remarks>
/// A page sets its own reviewed policy over these (<see cref="V3WebRoot"/>: the page's Content-Security-Policy with
/// <c>frame-ancestors 'none'</c>). Every other answer (the API's envelopes and problems, the MCP endpoint, readiness, the key
/// set) is no page, so its policy allows nothing at all (<see cref="MachineContentSecurityPolicy"/>), stricter than a policy
/// that defaults to self, and nothing may frame it. A transport problem clears the response before it writes, so it applies
/// these again (<see cref="V3TransportResponse"/>).
/// </remarks>
internal static class V3HostHeaders
{
    /// <summary>A year of HTTPS only, for the site's name and every name under it.</summary>
    internal const string StrictTransportSecurity = V3WebRoot.StrictTransportSecurity;

    /// <summary>No page or answer tells another site where the reader came from.</summary>
    internal const string ReferrerPolicy = "no-referrer";

    /// <summary>The policy of an answer that is no page: it loads nothing, runs nothing and is framed by no one.</summary>
    internal const string MachineContentSecurityPolicy = "default-src 'none'; " + V3WebRoot.FrameAncestors;

    /// <summary>Sets the host's headers on <paramref name="response"/>; a page then replaces the policy with its own.</summary>
    internal static void Apply(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var headers = response.Headers;
        headers.StrictTransportSecurity = StrictTransportSecurity;
        headers["Referrer-Policy"] = ReferrerPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = MachineContentSecurityPolicy;
    }
}
