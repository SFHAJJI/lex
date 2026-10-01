namespace Lex.V3.Contracts.Platform;

/// <summary>
/// The event names this pipeline may mint, from the closed event registry of the Stage 4 contract
/// (issue #348's frozen body, which names thirteen). Twelve are held here. The thirteenth, the
/// coverage event, is deliberately absent: B42's finding 5.2 is that a gate excusing itself with an
/// event the same pipeline mints is a real defect, and the scope-line suite fails if production
/// source names it. Whether the registry the API serves is these twelve, or the gate is amended to
/// admit a name that is never emitted, is before the owner.
/// </summary>
public static class V3EventRegistry
{
    public const string FirstSighting = "first_sighting";
    public const string Resighted = "resighted";
    public const string MetadataRevised = "metadata_revised";
    public const string ValidityRevised = "validity_revised";
    public const string IntervalClosed = "interval_closed";
    public const string WithdrawnFromSource = "withdrawn_from_source";
    public const string ExpressionAdded = "expression_added";
    public const string FileReplaced = "file_replaced";
    public const string RelationAsserted = "relation_asserted";
    public const string RelationRetracted = "relation_retracted";
    public const string FutureStateScheduled = "future_state_scheduled";
    public const string FutureStateActivated = "future_state_activated";

    /// <summary>The twelve mintable names, in the registry's own order.</summary>
    public static IReadOnlyList<string> Mintable { get; } = Array.AsReadOnly(new[]
    {
        FirstSighting,
        Resighted,
        MetadataRevised,
        ValidityRevised,
        IntervalClosed,
        WithdrawnFromSource,
        ExpressionAdded,
        FileReplaced,
        RelationAsserted,
        RelationRetracted,
        FutureStateScheduled,
        FutureStateActivated,
    });

    /// <summary>
    /// The events whose presence can invalidate a past dated answer, which is what <c>answer_drift</c>
    /// enumerates (31-v3-spec: for each validity_revised or interval_closed event, the (work, date)
    /// pairs whose as_of answer changed).
    /// </summary>
    public static IReadOnlyList<string> Revising { get; } = Array.AsReadOnly(new[] { ValidityRevised, IntervalClosed });

    /// <summary>
    /// The basis of a log built from one observation with no predecessor: it can hold only
    /// <see cref="FirstSighting"/>, and a revision needs a later build compared against it.
    /// </summary>
    public const string GenesisBasis = "genesis";

    /// <summary>The basis of a log carried forward from a predecessor's and appended to by later builds (predecessor chaining).</summary>
    public const string ChainedBasis = "chained";
}
