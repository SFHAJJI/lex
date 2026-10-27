using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
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

    /// <summary>The class an EU act outside the mounted EU population is refused under (R80); never one of the list's classes.</summary>
    internal const string EuropePopulationClass = "eu_act_outside_population";

    internal const string EuropePopulationInstrument = "an EU act outside the EU population this site holds";

    /// <summary>Who to ask about an EU act Lex does not hold: the Publications Office, through EUR-Lex, which publishes it.</summary>
    internal static readonly V3OutOfScopeWhoToAsk EuropePopulationWhoToAsk =
        new("Publications Office of the European Union (EUR-Lex)", "https://eur-lex.europa.eu/", "information");

    internal const string EuropePopulationWhatWouldAnswer =
        "Lex extending its EU population to this act, which it has not done: after the acts of its seed list, the rule takes next the EU acts " +
        "Legilux links to, then primary law (TEU, TFEU, Charter); the official place and who_to_ask name where the act is published";

    /// <summary>
    /// The one-sentence reason: the rule, with the count of seed acts the mounted EU index holds, and the act asked for.
    /// </summary>
    internal static string EuropePopulationReason(int seeds, string act) =>
        "Lex holds the EU acts its population rule selects, " + (seeds == 1 ? "the 1 act" : $"the {seeds.ToString(CultureInfo.InvariantCulture)} acts") +
        " of its seed list (Decision 84), each with its original wording and the consolidated versions EUR-Lex lists of it, and " + act +
        " is not one of them.";

    /// <summary>
    /// <c>out_of_corpus_scope</c> for an EU act the mounted EU population does not hold (the specification of record's R80: "An unheld EU
    /// act answers out_of_corpus_scope, naming the rule and linking EUR-Lex"), or null. Only the request's own <c>identifier</c> is
    /// read, as the out-of-scope list reads it, so a search's text is never refused; only when the EU index states its population
    /// (its census, with at least one seed act); and only for an act named by its CELEX (as typed, after "CELEX:", or in an
    /// EUR-Lex or Publications Office address) in a sector of acts (0 to 4), when the index holds nothing of it or of the act it
    /// belongs to (a consolidated version's or a corrigendum's). Every other identifier keeps <c>identifier_unknown</c>.
    /// </summary>
    private V3PlatformOperationOutcome? RefuseOutsideEuropePopulation(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt)
    {
        if (_europeReader is not { HasStates: true } ||
            !request.Parameters.TryGetProperty("identifier", out var value) || value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), identifier, StringComparison.Ordinal) ||
            EuropeActCelexOf(identifier) is not { } named)
        {
            return null;
        }

        var seeds = EuropeSeedCount();
        if (seeds == 0 || EuropeActHeld(named.Celex) || EuropeActHeld(named.Act))
        {
            return null;
        }

        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            out_of_scope_class = EuropePopulationClass,
            instrument = EuropePopulationInstrument,
            reason = EuropePopulationReason(seeds, named.Act),
            official_source = EuropePopulationOfficialSource(named.Act),
            who_to_ask = V3OutOfScope.WhoToAskPayload(EuropePopulationWhoToAsk),
            what_would_answer = OutOfScopeRoutes,
            what_would_answer_detail = EuropePopulationWhatWouldAnswer,
            asserts_absence_of_law = false,
            population_seed_acts = seeds,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, PublisherId.EuEurLex),
            new V3PlatformOperationRefusal(request, "out_of_corpus_scope", helpful.RootElement));
    }

    /// <summary>The act's page on EUR-Lex, in the reader's language (EUR-Lex's <c>AUTO</c>): a link only, never requested by Lex.</summary>
    internal static string EuropePopulationOfficialSource(string act) =>
        "https://eur-lex.europa.eu/legal-content/AUTO/?uri=CELEX:" + act;

    /// <summary>How many seed acts the mounted EU index states its population from: its census's distinct seed CELEX.</summary>
    private int EuropeSeedCount()
    {
        lock (_europeTimeGate)
        {
            return EuropeStatesLocked().Select(static state => state.SeedCelex).Distinct(StringComparer.Ordinal).Count();
        }
    }

    /// <summary>Whether the mounted EU index holds anything of the act a CELEX names: a seed's census, or a work it resolves.</summary>
    private bool EuropeActHeld(string celex) =>
        EuropeSeedsOf(celex).Count > 0 || _europeReader!.ResolveExact(celex).Count > 0;

    /// <summary>
    /// The CELEX an identifier names, as typed, after "CELEX:", or in an EUR-Lex (<c>uri=CELEX:</c>) or Publications Office
    /// (<c>/resource/celex/</c>) address, and the act it belongs to: a consolidated version's (sector 0, its date dropped) is its
    /// act's in sector 3, and a corrigendum's (<c>R(nn)</c>) is its act's. Null for anything else, and for the sectors that hold
    /// no acts (5 and above: preparatory documents, case law, national measures, questions).
    /// </summary>
    internal static (string Celex, string Act)? EuropeActCelexOf(string identifier)
    {
        var written = identifier.Trim();
        string? celex = null;
        if (written.StartsWith("CELEX:", StringComparison.OrdinalIgnoreCase))
        {
            celex = written["CELEX:".Length..].Trim();
        }
        else if (Uri.TryCreate(written, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            if (string.Equals(uri.Host, "eur-lex.europa.eu", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "www.eur-lex.europa.eu", StringComparison.OrdinalIgnoreCase))
            {
                var query = Uri.UnescapeDataString(uri.Query);
                var at = query.IndexOf("uri=CELEX:", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    var rest = query[(at + "uri=CELEX:".Length)..];
                    var end = rest.IndexOfAny(['&', '#']);
                    celex = end < 0 ? rest : rest[..end];
                }
            }
            else if (string.Equals(uri.Host, "publications.europa.eu", StringComparison.OrdinalIgnoreCase) &&
                     uri.AbsolutePath.StartsWith("/resource/celex/", StringComparison.Ordinal))
            {
                celex = Uri.UnescapeDataString(uri.AbsolutePath["/resource/celex/".Length..]);
            }
        }
        else if (!written.Contains('/', StringComparison.Ordinal) && !written.Contains('#', StringComparison.Ordinal))
        {
            celex = written;
        }

        if (celex is null || celex.Length is < 6 or > 40 || celex[0] is < '0' or > '4' || OfficialIdentifier.ProfileOf(celex) is null)
        {
            return null;
        }

        var act = celex;
        if (act[0] == '0')
        {
            var dated = act.IndexOf('-', StringComparison.Ordinal);
            act = "3" + (dated < 0 ? act[1..] : act[1..dated]);
        }

        var corrigendum = act.IndexOf("R(", 5, StringComparison.Ordinal);
        if (corrigendum > 0)
        {
            act = act[..corrigendum];
        }

        return OfficialIdentifier.ProfileOf(act) is null ? null : (celex, act);
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
