using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

/// <summary>
/// The two refusals every operation can give before it reads the mount: an instrument Lex never holds
/// (<c>out_of_corpus_scope</c>, from the checked-in list <see cref="V3OutOfScope"/>; the plan's item 6.10), and an operation
/// served from no data (<c>retrieval_mode_unavailable</c>, from <see cref="V3UnservedOperations"/>; the plan's item 8.6). Each is
/// a typed refusal inside a normal envelope, over REST and MCP alike, since both transports dispatch through
/// <see cref="V3ApiHandler.ExecuteFor"/>.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>What would answer an out-of-scope instrument, in the platform's closed vocabulary: Lex collecting its class.</summary>
    internal static readonly string[] OutOfScopeRoutes = ["expanded_official_scope"];

    internal const string OutOfScopeWhatWouldAnswer =
        "Lex collecting this class of instrument, which it does not: the official place and who_to_ask name where it is published";

    /// <summary>
    /// <c>out_of_corpus_scope</c> when the request's <c>identifier</c> names a class of instrument the out-of-scope list holds, with
    /// the four elements the registry mandates (the instrument asked for, its official place, the reason, who to ask) and the
    /// class, the instrument in words and the list's identity and digest; null for any other request. Attribution follows the
    /// class (a CSSF circular is Luxembourg's, an EU court's decision the EU's), never the mount.
    /// </summary>
    internal V3PlatformOperationOutcome? RefuseOutOfScope(V3PlatformOperationRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Parameters.TryGetProperty("identifier", out var value) || value.ValueKind != JsonValueKind.String ||
            V3OutOfScope.Recognise(value.GetString()) is not { } named)
        {
            return null;
        }

        var publisher = string.Equals(named.Jurisdiction, "eu", StringComparison.Ordinal) ? PublisherId.EuEurLex : PublisherId.LuLegilux;
        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = value.GetString(),
            out_of_scope_class = named.Class,
            instrument = named.Instrument,
            reason = named.Reason,
            official_source = named.OfficialSource,
            who_to_ask = V3OutOfScope.WhoToAskPayload(named.WhoToAsk),
            what_would_answer = OutOfScopeRoutes,
            what_would_answer_detail = OutOfScopeWhatWouldAnswer,
            asserts_absence_of_law = false,
            scope_list = V3OutOfScope.Schema,
            scope_list_sha256 = V3OutOfScope.Sha256,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher),
            new V3PlatformOperationRefusal(request, "out_of_corpus_scope", helpful.RootElement));
    }

    /// <summary>
    /// The answer of an operation served from no data (<see cref="V3UnservedOperations"/>), whatever it is asked:
    /// <c>retrieval_mode_unavailable</c>, naming the mode asked and the modes this server serves in the same lane, with the data
    /// that would serve it. Attribution follows the identifier when the request names one (an EU-shaped one is the EU's), and
    /// otherwise the mount's Luxembourg index, or the EU's when only that is mounted.
    /// </summary>
    internal V3PlatformOperationOutcome Unserved(V3PlatformOperationRequest request, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = V3UnservedOperations.Rows.Single(candidate => string.Equals(candidate.Operation, request.OperationId, StringComparison.Ordinal));
        var publisher = request.Parameters.TryGetProperty("identifier", out var identifier) &&
                        identifier.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(identifier.GetString())
            ? PublisherFor(identifier.GetString()!)
            : _reader is null ? PublisherId.EuEurLex : PublisherId.LuLegilux;
        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_mode = V3UnservedOperations.RequestedMode(row.Operation),
            available_modes = V3UnservedOperations.AvailableModes(row.Operation),
            data_needed = row.DataNeeded,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher),
            new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", helpful.RootElement));
    }
}
