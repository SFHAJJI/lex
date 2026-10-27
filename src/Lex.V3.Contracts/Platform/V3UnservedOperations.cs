using Lex.V3.Contracts.Index;

namespace Lex.V3.Contracts.Platform;

/// <summary>
/// The registered operations the platform does not serve from data, each with the typed reason every request for it answers
/// and the data that would serve it. Since 2026-10-27 (the plan's item 8.6; the specification of record, §3, API and MCP: "All
/// 27 registered operations are served, or refuse with the typed reason their capability manifest states") each one is routed
/// over REST and MCP and refuses every request with the registry's <c>retrieval_mode_unavailable</c> inside a normal envelope,
/// naming the mode asked (<see cref="RequestedMode"/>) and the modes this server serves nearest it (<see cref="AvailableModes"/>);
/// before, a request answered the transport failure <c>operation_not_served</c> below the envelope, which manifests written
/// then still state (<see cref="V3UnservedOperation.OperationNotServed"/>). The data are the specification's own
/// (<c>33-product-spec.md</c>). One table, read by the index builders, which state it in each capability manifest they write,
/// and by the API, which routes, refuses and reports it in its <c>coverage</c> answer; a served operation never appears here,
/// and the API's tests hold this table to exactly the registered operations it does not serve.
/// </summary>
public static class V3UnservedOperations
{
    public static IReadOnlyList<V3UnservedOperation> Rows { get; } = Array.AsReadOnly(new[]
    {
        new V3UnservedOperation(
            "concepts",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the concept data attached to EU works: EuroVoc descriptors, EU directory codes and subject matters as the Publications Office " +
            "records them; the EU index holds none of them"),
        new V3UnservedOperation(
            "knowable_on",
            V3UnservedOperation.RetrievalModeUnavailable,
            "each held state's publication date beside its observation time (observed_from), so a date is answered with what a reader " +
            "could have known on it, never with the publisher's valid-from date; the observation times need each Luxembourg body's capture time " +
            "in the corpus, which no build records yet: a build's time bounds observation only from above"),
        new V3UnservedOperation(
            "transposition",
            V3UnservedOperation.RetrievalModeUnavailable,
            "the transposition links: Legilux's transposes and draftTransposes assertions and the Publications Office's national implementing " +
            "measures for Luxembourg, each kept as its publisher asserts it and never merged; neither is acquired"),
    });

    /// <summary>
    /// The retrieval mode each unserved operation is, by the specification's lanes (<c>34-ideal-architecture.md</c> §3: R4, the
    /// relation walk, which names "transposes"; R5, the classification browse, which names EuroVoc and directory codes; R6, the
    /// temporal query, which names <c>knowable_on</c>), in the tag form the platform's other <c>retrieval_mode_unavailable</c>
    /// refusals use.
    /// </summary>
    public static string RequestedMode(string operation) => operation switch
    {
        "concepts" => "r5_concepts",
        "knowable_on" => "r6_knowable_on",
        "transposition" => "r4_transposition",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not an unserved operation."),
    };

    /// <summary>
    /// The modes this server serves in the same lane, which a caller may ask instead, none of which answers the question the
    /// unserved operation asks: for <c>concepts</c>, the held works by the publisher's document types (<c>browse</c>); for
    /// <c>knowable_on</c>, the state applicable on a date by the publisher's dates (<c>as_of</c>) and the state one build of the log
    /// held (<c>as_observed</c>), whose build time bounds observation only from above; for <c>transposition</c>, the reference
    /// edges the publisher marked up (<c>citation</c>, <c>cited_by</c>, <c>relations</c>).
    /// </summary>
    public static IReadOnlyList<string> AvailableModes(string operation) => operation switch
    {
        "concepts" => ["r5_browse"],
        "knowable_on" => ["r6_as_of", "r6_as_observed"],
        "transposition" => ["r4_citation", "r4_cited_by", "r4_relations"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not an unserved operation."),
    };
}
