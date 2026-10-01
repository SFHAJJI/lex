using Lex.V3.Contracts.Index;

namespace Lex.V3.Contracts.Platform;

/// <summary>
/// The registered operations no route serves, each with the data that would serve it (the driver decision in STATUS:
/// they keep the transport failure <c>operation_not_served</c>, and the platform says, per operation, which data would
/// serve it). The data are the specification's own (<c>33-product-spec.md</c>). One table, read by the index builders,
/// which state it in each capability manifest they write, and by the API's <c>coverage</c> answer; a served operation
/// never appears here, and the API's tests hold this table to exactly the registered operations it has no route for.
/// </summary>
public static class V3UnservedOperations
{
    public static IReadOnlyList<V3UnservedOperation> Rows { get; } = Array.AsReadOnly(new[]
    {
        new V3UnservedOperation(
            "concepts",
            "the concept data attached to EU works: EuroVoc descriptors, EU directory codes and subject matters as the Publications Office " +
            "records them; the EU index holds none of them"),
        new V3UnservedOperation(
            "knowable_on",
            "each held state's publication date beside its observation time (observed_from), so a date is answered with what a reader " +
            "could have known on it, never with the publisher's valid-from date; the observation times need each Luxembourg body's capture time " +
            "in the corpus, which no build records yet: a build's time bounds observation only from above"),
        new V3UnservedOperation(
            "transposition",
            "the transposition links: Legilux's transposes and draftTransposes assertions and the Publications Office's national implementing " +
            "measures for Luxembourg, each kept as its publisher asserts it and never merged; neither is acquired"),
    });
}
