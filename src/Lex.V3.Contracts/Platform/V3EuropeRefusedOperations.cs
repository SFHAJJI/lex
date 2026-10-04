using Lex.V3.Contracts.Index;

namespace Lex.V3.Contracts.Platform;

/// <summary>
/// The registered operations routed for Luxembourg works that refuse an EU identifier, each with the typed reason a request
/// answers (<see cref="V3UnservedOperation.RetrievalModeUnavailable"/>, naming the requested mode and the modes available) and
/// the EU data that would serve it. The launch contract's line "each either served or refusing with a typed reason its
/// capability manifest states": the EU index builder states this table in every EU capability manifest it writes, beside
/// <see cref="V3UnservedOperations"/>, the API's <c>coverage</c> answer reports the mounted EU manifest's statement, and the
/// API's tests hold each row to the refusal an EU identifier receives. An operation the EU time view serves for an EU act
/// (<c>as_of</c>, <c>timeline</c>, <c>evidence_bundle</c>, <c>diff</c>, <c>verify</c> of a pinned EU permalink, <c>dossier</c>,
/// <c>search</c> in one EU work, <c>resolve</c>) never appears here.
/// </summary>
public static class V3EuropeRefusedOperations
{
    public static IReadOnlyList<V3UnservedOperation> Rows { get; } = Array.AsReadOnly(new[]
    {
        new V3UnservedOperation(
            "answer_drift",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the EU index's builds recorded in the event log, whose revising events would name the EU answers a later consolidation " +
            "invalidated; the log records Luxembourg's builds only, and timeline serves an EU act's wordings by their dates"),
        new V3UnservedOperation(
            "article_history",
            V3UnservedOperation.RetrievalModeUnavailable,
            "one article followed across an EU act's dated wordings, matched by the publisher's article identifier from wording to " +
            "wording; the wordings are held (timeline and as_of serve each wording, evidence_bundle its articles), and this build does " +
            "not derive the alignment"),
        new V3UnservedOperation(
            "as_observed",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the EU index's builds recorded in the event log, so that a past build's EU wordings could be named by its index digest; the " +
            "log records Luxembourg's builds only"),
        new V3UnservedOperation(
            "changes_in_period",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the EU wordings dated in a period, each compared with the wording it follows; timeline lists each act's wordings by their " +
            "dates, and this build does not derive the comparison"),
        new V3UnservedOperation(
            "citation",
            V3UnservedOperation.RetrievalModeUnavailable,
            "a route that serves the references the publisher marked up in an EU wording's Formex text, with their targets resolved " +
            "to acts; the references are extracted with each article (its reference tokens) and held in the EU index, and no route " +
            "serves them or resolves their targets"),
        new V3UnservedOperation(
            "cited_by",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the references whose target is an EU act, from the held EU wordings and Luxembourg states, resolved to the act's EU " +
            "identifiers (its CELEX, Cellar and ELI forms); no reference target is resolved to an EU act"),
        new V3UnservedOperation(
            "classification",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the Publications Office's typed facts about an EU work (resource type, author, form and dates), verbatim, as an index " +
            "field; the census retains a few of them per discovered work, and this build does not serve them as a classification"),
        new V3UnservedOperation(
            "in_force_on",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the entry-into-force and application dates the Publications Office asserts for EU acts, indexed: the census acquires " +
            "them as reified date axioms and keeps them in custody, and no build indexes them; an EU wording date is never one of " +
            "them, so the time view cannot answer an applicability question"),
        new V3UnservedOperation(
            "manifestation",
            V3UnservedOperation.RetrievalModeUnavailable,
            "an EU expression's manifestations with their formats, the retained one marked with its body digest, as one answer; " +
            "acquisition enumerates them, and this build serves the retained bodies' digests in evidence_bundle but not the listing"),
        new V3UnservedOperation(
            "provenance",
            V3UnservedOperation.RetrievalModeUnavailable,
            "a dated EU wording's retained sources, rule profiles and digests in provenance's shape; evidence_bundle carries an EU " +
            "wording's corpus members and digests, and this build does not compose the provenance record"),
        new V3UnservedOperation(
            "relations",
            V3UnservedOperation.RetrievalModeUnavailable,
            "a route that serves an EU work's reference edges in both directions: the outbound edges are its articles' reference " +
            "tokens, held in the EU index, and the inbound need every held text's references resolved to EU acts; neither direction " +
            "is served"),
        new V3UnservedOperation(
            "status_on",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the Publications Office's force assertions about an EU work (in force, entry into force, end of validity), verbatim and " +
            "indexed: the census acquires them (its in-force fact and reified date axioms) and keeps them in custody, and no build " +
            "indexes them"),
    });
}
