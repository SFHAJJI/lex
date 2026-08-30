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

public static class V3SchemaResourceIds
{
    public const string PreviewArtifact = "urn:uuid:94b202e8-f515-45e0-8891-6cbee0f2d32b";
    public const string PreviewPayload = "urn:uuid:f6c0e06f-1140-48da-8aa2-0195d5377f5d";
    public const string PreviewEnvelope = "urn:uuid:caee1a0e-fe35-4c42-bf0e-5ad5106114c0";
    public const string PreviewObjectSet = "urn:uuid:87e642f4-501b-4a0f-a124-2906f8b4b831";
    public const string PreviewOperationCatalog = "urn:uuid:9771ad74-c254-4b4d-9bb3-c8fbc71befba";
    public const string PreviewRefusalRegistry = "urn:uuid:28beec07-21f2-4049-bd87-58fb6dacfb7a";

    public static string ForWireSchema(string schema) => schema switch
    {
        V3SchemaIds.PreviewArtifact => PreviewArtifact,
        V3SchemaIds.PreviewPayload => PreviewPayload,
        V3SchemaIds.PreviewEnvelope => PreviewEnvelope,
        V3SchemaIds.PreviewObjectSet => PreviewObjectSet,
        V3SchemaIds.PreviewOperationCatalog => PreviewOperationCatalog,
        V3SchemaIds.PreviewRefusalRegistry => PreviewRefusalRegistry,
        _ => throw new ArgumentException("Unknown preview schema identity.", nameof(schema)),
    };
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

public enum PublisherId
{
    [JsonStringEnumMemberName("lu-legilux")]
    LuLegilux,

    [JsonStringEnumMemberName("eu-eurlex")]
    EuEurLex,
}

public enum TimelineSemantics
{
    [JsonStringEnumMemberName("publisher_applicability")]
    PublisherApplicability,

    [JsonStringEnumMemberName("official_consolidation_state")]
    OfficialConsolidationState,
}

public enum BodyHoldingState
{
    [JsonStringEnumMemberName("held_public")]
    HeldPublic,

    [JsonStringEnumMemberName("held_withheld")]
    HeldWithheld,

    [JsonStringEnumMemberName("not_held")]
    NotHeld,
}

public enum PreviewBodyDispositionReason
{
    [JsonStringEnumMemberName("synthetic_fixture")]
    SyntheticFixture,

    [JsonStringEnumMemberName("synthetic_fixture_withheld")]
    SyntheticFixtureWithheld,

    [JsonStringEnumMemberName("unknown_pending_evidence")]
    UnknownPendingEvidence,
}

public enum PreviewUpstreamHealth
{
    [JsonStringEnumMemberName("not_applicable_synthetic")]
    NotApplicableSynthetic,
}

public enum RetrievalOutcome
{
    [JsonStringEnumMemberName("metadata_only")]
    MetadataOnly,
}

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

public enum RefusalCode
{
    [JsonStringEnumMemberName("identifier_unknown")]
    IdentifierUnknown,
}

public enum WhatWouldAnswerAction
{
    [JsonStringEnumMemberName("corrected_identifier")]
    CorrectedIdentifier,

    [JsonStringEnumMemberName("new_official_observation")]
    NewOfficialObservation,

    [JsonStringEnumMemberName("expanded_official_scope")]
    ExpandedOfficialScope,
}
