using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

public enum LuScopeTerminalState
{
    [JsonStringEnumMemberName("accepted_metadata")]
    AcceptedMetadata,

    [JsonStringEnumMemberName("accepted_candidate")]
    AcceptedCandidate,

    [JsonStringEnumMemberName("point")]
    Point,

    [JsonStringEnumMemberName("never_ingest")]
    NeverIngest,

    [JsonStringEnumMemberName("typed_quarantine")]
    TypedQuarantine,

    [JsonStringEnumMemberName("missing_publisher_value")]
    MissingPublisherValue,

    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuScopeDimensionDisposition
{
    private const string NotApplicableReasonPrefix = "not_applicable_";

    [JsonConstructor]
    public LuScopeDimensionDisposition(
        LuScopeTerminalState state,
        string reasonCode,
        string ruleId,
        IReadOnlyList<string> evidenceIds)
    {
        State = ContractValidation.RequireDefined(state, nameof(state));
        ReasonCode = RequireCode(reasonCode, nameof(reasonCode));
        RuleId = RequireCode(ruleId, nameof(ruleId));

        if (state == LuScopeTerminalState.NotApplicable &&
            (!reasonCode.StartsWith(NotApplicableReasonPrefix, StringComparison.Ordinal) ||
             string.IsNullOrWhiteSpace(reasonCode[NotApplicableReasonPrefix.Length..])))
        {
            throw new ArgumentException(
                "A not-applicable disposition requires an explicit not_applicable_* reason.",
                nameof(reasonCode));
        }

        if (state != LuScopeTerminalState.NotApplicable &&
            reasonCode.StartsWith(NotApplicableReasonPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A not_applicable_* reason is valid only for a not-applicable disposition.",
                nameof(reasonCode));
        }

        var copy = (evidenceIds ?? throw new ArgumentNullException(nameof(evidenceIds))).ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(copy[index]))
            {
                throw new ArgumentException(
                    "Evidence identifiers cannot be null, empty, or whitespace.",
                    nameof(evidenceIds));
            }

            if (index > 0 && StringComparer.Ordinal.Compare(copy[index - 1], copy[index]) >= 0)
            {
                throw new ArgumentException(
                    "Evidence identifiers must be ordinal-sorted and duplicate-free.",
                    nameof(evidenceIds));
            }
        }

        EvidenceIds = Array.AsReadOnly(copy);
    }

    public LuScopeTerminalState State { get; }

    public string ReasonCode { get; }

    public string RuleId { get; }

    public IReadOnlyList<string> EvidenceIds { get; }

    private static string RequireCode(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Disposition codes cannot be empty or whitespace.",
                parameterName);
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LuScopeDimensions
{
    [JsonConstructor]
    public LuScopeDimensions(
        LuScopeDimensionDisposition @record,
        LuScopeDimensionDisposition body,
        LuScopeDimensionDisposition relation,
        LuScopeDimensionDisposition supportingDocument,
        LuScopeDimensionDisposition publicationFamily,
        LuScopeDimensionDisposition language,
        LuScopeDimensionDisposition format,
        LuScopeDimensionDisposition authenticity,
        LuScopeDimensionDisposition rights,
        LuScopeDimensionDisposition transport)
    {
        Record = @record ?? throw new ArgumentNullException(nameof(@record));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Relation = relation ?? throw new ArgumentNullException(nameof(relation));
        SupportingDocument = supportingDocument ?? throw new ArgumentNullException(nameof(supportingDocument));
        PublicationFamily = publicationFamily ?? throw new ArgumentNullException(nameof(publicationFamily));
        Language = language ?? throw new ArgumentNullException(nameof(language));
        Format = format ?? throw new ArgumentNullException(nameof(format));
        Authenticity = authenticity ?? throw new ArgumentNullException(nameof(authenticity));
        Rights = rights ?? throw new ArgumentNullException(nameof(rights));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public LuScopeDimensionDisposition Record { get; }

    public LuScopeDimensionDisposition Body { get; }

    public LuScopeDimensionDisposition Relation { get; }

    public LuScopeDimensionDisposition SupportingDocument { get; }

    public LuScopeDimensionDisposition PublicationFamily { get; }

    public LuScopeDimensionDisposition Language { get; }

    public LuScopeDimensionDisposition Format { get; }

    public LuScopeDimensionDisposition Authenticity { get; }

    public LuScopeDimensionDisposition Rights { get; }

    public LuScopeDimensionDisposition Transport { get; }
}
