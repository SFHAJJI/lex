using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

public static class V3SchemaIds
{
    public const string PreviewArtifact = "lex-v3-preview-artifact/1";
    public const string PreviewPayload = "lex-v3-preview-payload/1";
    public const string PreviewEnvelope = "lex-v3-preview-envelope/1";
    public const string PreviewObjectSet = "lex-v3-preview-object-set/1";
    public const string PreviewOperationCatalog = "lex-v3-preview-operation-catalog/1";
    public const string PreviewRefusalRegistry = "lex-v3-preview-refusal-registry/1";
    public const string PreviewArtifactSignature = "lex-v3-preview-artifact-signature/1";
}

public static class V3ContractVocabulary
{
    public static ReadOnlyCollection<string> CoreObjectTypes { get; } = Array.AsReadOnly(
        new[]
        {
            "envelope",
            "quote",
            "version_state",
            "timeline",
            "provision_history",
            "diff",
            "relation_edge",
            "classification",
            "refusal",
            "provenance_chain",
            "coverage_report",
        });

    public static ReadOnlyCollection<string> CompositionTypes { get; } = Array.AsReadOnly(
        new[]
        {
            "work_record",
            "work_resolution",
            "change_list",
            "resolution_verdict",
            "evidence_bundle",
            "answer_dossier",
            "handoff_card",
        });

    public static ReadOnlyCollection<string> OperationIds { get; } = Array.AsReadOnly(
        new[]
        {
            "resolve",
            "search",
            "browse",
            "concepts",
            "dossier",
            "manifestation",
            "as_of",
            "as_observed",
            "knowable_on",
            "timeline",
            "article_history",
            "diff",
            "status_on",
            "in_force_on",
            "changes_in_period",
            "relations",
            "classification",
            "cited_by",
            "citation",
            "transposition",
            "provenance",
            "verify",
            "evidence_bundle",
            "events",
            "answer_drift",
            "coverage",
            "ask",
        });
}

[JsonConverter(typeof(JsonStringEnumConverter<PublisherId>))]
public enum PublisherId
{
    [JsonStringEnumMemberName("lu-legilux")]
    LuLegilux,

    [JsonStringEnumMemberName("eu-eurlex")]
    EuEurLex,
}

[JsonConverter(typeof(JsonStringEnumConverter<TimelineSemantics>))]
public enum TimelineSemantics
{
    [JsonStringEnumMemberName("publisher_applicability")]
    PublisherApplicability,

    [JsonStringEnumMemberName("official_consolidation_state")]
    OfficialConsolidationState,
}

[JsonConverter(typeof(JsonStringEnumConverter<BodyHoldingState>))]
public enum BodyHoldingState
{
    [JsonStringEnumMemberName("held_public")]
    HeldPublic,

    [JsonStringEnumMemberName("held_withheld")]
    HeldWithheld,

    [JsonStringEnumMemberName("not_held")]
    NotHeld,
}

[JsonConverter(typeof(JsonStringEnumConverter<RetrievalOutcome>))]
public enum RetrievalOutcome
{
    [JsonStringEnumMemberName("metadata_only")]
    MetadataOnly,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdentifierFamily>))]
public enum IdentifierFamily
{
    [JsonStringEnumMemberName("eli")]
    Eli,

    [JsonStringEnumMemberName("celex")]
    Celex,

    [JsonStringEnumMemberName("memorial")]
    Memorial,

    [JsonStringEnumMemberName("historical_legal_id")]
    HistoricalLegalId,
}

[JsonConverter(typeof(JsonStringEnumConverter<RefusalCode>))]
public enum RefusalCode
{
    [JsonStringEnumMemberName("identifier_unknown")]
    IdentifierUnknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<WhatWouldAnswerAction>))]
public enum WhatWouldAnswerAction
{
    [JsonStringEnumMemberName("corrected_identifier")]
    CorrectedIdentifier,

    [JsonStringEnumMemberName("new_official_observation")]
    NewOfficialObservation,

    [JsonStringEnumMemberName("expanded_official_scope")]
    ExpandedOfficialScope,
}
