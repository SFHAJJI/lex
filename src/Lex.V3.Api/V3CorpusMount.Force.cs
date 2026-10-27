using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// What the Luxembourg dossier says of the act's force (the specification of record's R79, E3; the plan's item for the dossier):
/// the publisher's own assertions about the act as a whole, its status token, its entry into force and the date it is no longer in
/// force, read verbatim from the facts <c>status_on</c> already serves, for the work and its act as published only. Nothing is read
/// into them: the status token is the publisher's word, shown as it is; a date is said with its role, never as an interval in
/// force (R21); and the dossier gives no reading for a date, which is <c>status_on</c>'s and never legal advice.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string DossierForceRule =
        "the publisher's own assertions about this act as a whole, on its work and its act as published (the work IRI followed by /jo), " +
        "read verbatim from the index's fact table: in_force_status (jolux:inForceStatus, the publisher's token, not read), entry_into_force " +
        "(jolux:dateEntryInForce) and no_longer_in_force (jolux:dateNoLongerInForce), each with its subject, value, datatype and evidence " +
        "digest; an empty list says only that the index holds no such assertion; a consolidated version's own dates are its states' " +
        "(applicability_date, applicable_to), a different fact; nothing here says whether the act applies to anyone, and status_on reads " +
        "these dates for one date by a fixed rule";

    /// <summary>
    /// A Luxembourg dossier with its act's force facts (<see cref="DossierForceRule"/>), on lex-v3-luxembourg-index/9 only: an
    /// index of schema 8 or 6 answers as it always did.
    /// </summary>
    private object WithDossierForce(object result, IReadOnlyList<LuxembourgIndexResolvedState> states)
    {
        if (!RulesByInterval)
        {
            return result;
        }

        var work = states[0].PublisherWorkIri;
        var subjects = new[] { work, work + "/jo" };
        var facts = _reader!.ResolveFacts(subjects);
        FactView[] Of(LuxembourgAssertionPredicate predicate) =>
            subjects.SelectMany(subject => FactsOf(facts, predicate, subject)).ToArray();

        var node = result as JsonObject ?? JsonSerializer.SerializeToNode(result)!.AsObject();
        node["force"] = JsonSerializer.SerializeToNode(new
        {
            subjects,
            in_force_status = Of(LuxembourgAssertionPredicate.InForceStatus),
            entry_into_force = Of(LuxembourgAssertionPredicate.DateEntryInForce),
            no_longer_in_force = Of(LuxembourgAssertionPredicate.DateNoLongerInForce),
            rule = DossierForceRule,
        });
        return node;
    }
}
