using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// The document-fetch route's own closed refusal vocabulary for a completed GET whose terminal
/// status was not 200. SCOPE_RULING lex-event-20260904T104723233Z-fa84c4edb4144467a2a63c94ee469cef
/// item 5: "The 400 and 404 refusals are closed members of the route's vocabulary carrying the
/// observed status" -- named business-level members, not a generic HTTP-status wrapper.
/// </summary>
public enum EuDocumentFetchRefusal
{
    /// <summary>
    /// HTTP 400. PROVEN (<c>review/23-research-temporal.md</c> section 1.2): <c>Accept:
    /// application/pdf;mtype=pdfa1a</c> returned 400 because the spec uses <c>type=</c> for PDF and
    /// <c>mtype=</c> for zip packages -- the wrong content-negotiation token for the manifestation
    /// family requested.
    /// </summary>
    [JsonStringEnumMemberName("wrong_accept_token")]
    WrongAcceptToken = 1,

    /// <summary>HTTP 404. PROVEN: a manifestation that does not exist for this object.</summary>
    [JsonStringEnumMemberName("manifestation_not_found")]
    ManifestationNotFound = 2,
}

/// <summary>
/// Classifies a completed document-fetch <see cref="RoutedHttpEvidence"/> route into the closed
/// 400/404 business vocabulary, or reports that it is neither (a genuine 200, or any other
/// terminal status this classifier does not name). This is a pure reader over already-sealed
/// evidence; it retains nothing and mints no new custody.
/// </summary>
public sealed class EuDocumentFetchOutcome
{
    private EuDocumentFetchOutcome(EuDocumentFetchRefusal? refusal, int? observedStatus)
    {
        Refusal = refusal;
        ObservedStatus = observedStatus;
    }

    /// <summary>The closed refusal this route completed as, or null when it is not one of the two.</summary>
    public EuDocumentFetchRefusal? Refusal { get; }

    /// <summary>
    /// The real observed terminal status, present whenever the route completed at all (a redirect
    /// exhaustion, robots refusal, or transport failure carries no terminal status here).
    /// </summary>
    public int? ObservedStatus { get; }

    /// <summary>The only path that classifies. Reads the evidence; mints nothing.</summary>
    public static EuDocumentFetchOutcome Classify(RoutedHttpEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Outcome is not CompleteHttpRouteOutcome || evidence.Hops.Count == 0)
        {
            return new EuDocumentFetchOutcome(null, null);
        }

        var terminal = evidence.Hops[^1];
        return terminal.Status switch
        {
            400 => new EuDocumentFetchOutcome(EuDocumentFetchRefusal.WrongAcceptToken, 400),
            404 => new EuDocumentFetchOutcome(EuDocumentFetchRefusal.ManifestationNotFound, 404),
            _ => new EuDocumentFetchOutcome(null, terminal.Status),
        };
    }
}
