using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lex.Tests;

public sealed class TruthContractDocumentationTests
{
    [Fact]
    public void Revalidation_contract_publishes_the_three_cadences_and_fail_closed_rules()
    {
        var text = NormalizeWhitespace(Read("docs", "corpus-revalidation.md"));

        Assert.Contains("Nightly", text, StringComparison.Ordinal);
        Assert.Contains("Weekly", text, StringComparison.Ordinal);
        Assert.Contains("Monthly", text, StringComparison.Ordinal);
        Assert.Contains("every open state and every future-dated state", text,
            StringComparison.Ordinal);
        Assert.Contains("every held manifestation whose official URI still resolves", text,
            StringComparison.Ordinal);
        Assert.Contains("GET only. It never uses HEAD.", text, StringComparison.Ordinal);
        Assert.Contains("A 304 is a completed revalidation", text, StringComparison.Ordinal);
        Assert.Contains("1,500 ms", text, StringComparison.Ordinal);
        Assert.Contains("three distinct completed run identities", text, StringComparison.Ordinal);
        Assert.Contains("one-million-row truncation guard", text, StringComparison.Ordinal);
        Assert.Contains("No absence event is appended", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Machine_contract_freezes_revalidation_fallback_and_logical_run_identity()
    {
        var contract = Contract();
        var revalidation = RequiredObject(contract, "revalidation");
        var cadences = RequiredObject(revalidation, "cadences");
        var http = RequiredObject(revalidation, "http");
        var identity = RequiredObject(revalidation, "run_identity");
        var slots = RequiredObject(identity, "slot_formats");

        Assert.Equal("lex-truth-contract/1", RequiredString(contract, "schema"));
        Assert.Equal("accepted_v3_target_not_deployed", RequiredString(contract, "status"));
        Assert.Equal("live_read_back_required_before_claim", RequiredString(contract, "enforcement"));
        Assert.Equal(
            ["publisher_feed", "open_states", "future_dated_states"],
            RequiredStrings(cadences, "nightly"));
        Assert.Equal(
            ["complete_reviewed_lu_catalog", "complete_reviewed_eu_catalog"],
            RequiredStrings(cadences, "weekly"));
        Assert.Equal(
            ["every_still_resolving_held_manifestation"],
            RequiredStrings(cadences, "monthly"));
        Assert.Equal("GET", RequiredString(http, "method"));
        Assert.Equal(["HEAD"], RequiredStrings(http, "forbidden_methods"));
        Assert.Equal(
            ["etag", "last_modified", "unconditional_get"],
            RequiredStrings(http, "validator_order"));
        Assert.Equal("unconditional_get", RequiredString(http, "missing_validator_outcome"));
        Assert.Equal("completed_revalidation", RequiredString(http, "not_modified_outcome"));
        Assert.Equal("sha256_rfc8785", RequiredString(identity, "algorithm"));
        Assert.Equal(
            ["publisher", "cadence", "scheduled_slot_utc", "scope_manifest_sha256"],
            RequiredStrings(identity, "fields"));
        Assert.True(RequiredBoolean(identity, "scope_frozen_for_slot"));
        Assert.True(RequiredBoolean(identity, "retry_must_reuse_identity"));
        Assert.Equal(
            ["attempt", "process_id", "wall_clock_start", "random_uuid"],
            RequiredStrings(identity, "excluded_fields"));
        Assert.Equal("YYYY-MM-DD", RequiredString(slots, "nightly"));
        Assert.Equal("YYYY-Www", RequiredString(slots, "weekly"));
        Assert.Equal("YYYY-MM", RequiredString(slots, "monthly"));
        Assert.Equal(
            [
                "feed_complete_if_nightly",
                "target_set_complete",
                "pagination_complete",
                "publisher_truncation_checks_passed",
                "identity_coherent"
            ],
            RequiredStrings(revalidation, "completion_requires"));
        Assert.Equal(
            "distinct_completed_logical_run_identity_only",
            RequiredString(revalidation, "absence_advances_on"));
    }

    [Fact]
    public void Machine_contract_is_an_explicit_fail_closed_target_interface()
    {
        var binding = RequiredObject(Contract(), "implementation_binding");

        Assert.Equal("exact_schema_and_values", RequiredString(binding, "consumer_validation"));
        Assert.Equal("contract_rejected", RequiredString(binding, "unknown_field_outcome"));
        Assert.Equal("contract_rejected", RequiredString(binding, "missing_field_outcome"));
        Assert.Equal("contract_rejected", RequiredString(binding, "unknown_value_outcome"));
        Assert.Equal("claim_blocked", RequiredString(binding, "missing_live_read_back_outcome"));
    }

    [Fact]
    public void Revalidation_prose_defines_validator_fallback_and_retry_safe_completion()
    {
        var source = Read("docs", "corpus-revalidation.md");
        ValidateRevalidationContractText(source);
        var text = NormalizeWhitespace(source);

        Assert.Contains(
            "If neither validator exists, Lex performs an unconditional GET; it never substitutes HEAD.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "The scope manifest is frozen for that slot, and every retry must reuse the same identity.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "A nightly run is incomplete unless feed processing and the open and future-state enumeration both complete.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Every required component of all three cadences, not only catalog enumeration, must complete",
            text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("When validators are absent, Lex uses HEAD.")]
    [InlineData("A retry may mint a new run identity.")]
    [InlineData("A nightly run may complete after feed failure.")]
    public void Revalidation_text_validator_rejects_contradictory_clauses(string contradiction)
    {
        var text = Read("docs", "corpus-revalidation.md") + "\n" + contradiction + "\n";

        Assert.Throws<InvalidDataException>(() => ValidateRevalidationContractText(text));
    }

    [Fact]
    public void Snapshot_contract_publishes_exact_retention_and_keeper_selection()
    {
        var text = NormalizeWhitespace(Read("docs", "snapshot-retention.md"));

        Assert.Contains("referenced by an issued evidence bundle", text, StringComparison.Ordinal);
        Assert.Contains("retained indefinitely", text, StringComparison.Ordinal);
        Assert.Contains("Nightly releases are retained for 90 days", text, StringComparison.Ordinal);
        Assert.Contains("appended latest in that UTC month", text, StringComparison.Ordinal);
        Assert.Contains("lexicographically smallest manifest-set identifier", text,
            StringComparison.Ordinal);
        Assert.Contains("no_eligible_release", text, StringComparison.Ordinal);
        Assert.Contains("cannot be replaced or deleted", text, StringComparison.Ordinal);
        Assert.Contains("canon ID", text, StringComparison.Ordinal);
        Assert.Contains("Unsigned generated prose is not replayable", text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Observation history begins August 2026; replay depth grows from here.", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Machine_contract_serializes_reference_publication_and_every_cleanup_delete()
    {
        var retention = RequiredObject(Contract(), "retention");
        var classes = RequiredObject(retention, "classes");
        var clock = RequiredObject(retention, "clock");
        var monthly = RequiredObject(retention, "monthly_selection");
        var mutation = RequiredObject(retention, "mutation_protocol");

        Assert.Equal("indefinite", RequiredString(classes, "evidence_bundle_release"));
        Assert.Equal("minimum_2160_hours", RequiredString(classes, "nightly_release"));
        Assert.Equal("indefinite", RequiredString(classes, "monthly_keeper"));
        Assert.Equal("signed_ledger_accepted_at_utc", RequiredString(clock, "source"));
        Assert.Equal(
            "cleanup_executor_trusted_utc_clock",
            RequiredString(clock, "deletion_now_source"));
        Assert.Equal("no_deletion", RequiredString(clock, "clock_failure_outcome"));
        Assert.Equal("rfc3339_utc", RequiredString(clock, "format"));
        Assert.False(RequiredBoolean(clock, "caller_timestamp_allowed"));
        Assert.True(RequiredBoolean(clock, "monotonic_against_ledger_head"));
        Assert.Equal(2160, RequiredInt32(clock, "nightly_duration_hours"));
        Assert.Equal(
            "now_utc_gte_accepted_at_utc_plus_2160_hours",
            RequiredString(clock, "delete_boundary"));
        Assert.Equal(
            ["accepted_at_utc_desc", "manifest_set_identifier_asc"],
            RequiredStrings(monthly, "selection_order"));
        Assert.True(RequiredBoolean(monthly, "no_eligible_release_is_final"));
        Assert.False(RequiredBoolean(monthly, "selection_replaceable"));
        Assert.Equal(
            [
                "complete_lu_artifact",
                "complete_eu_artifact",
                "valid_signatures",
                "complete_retention_receipt",
                "capability_manifest",
                "evaluation_identity",
                "exact_build_identity"
            ],
            RequiredStrings(monthly, "eligibility_requires"));

        Assert.Equal("lex-retention-ledger/1", RequiredString(mutation, "cooperative_lock"));
        Assert.Equal(
            ["bundle_reference_publication", "monthly_selection", "cleanup_deletion"],
            RequiredStrings(mutation, "lock_users"));
        Assert.True(RequiredBoolean(mutation, "fencing_token_required"));
        Assert.True(RequiredBoolean(mutation, "ledger_head_cas_required"));
        Assert.True(RequiredBoolean(mutation, "bundle_reference_committed_before_return"));
        Assert.True(RequiredBoolean(mutation, "cleanup_authorization_committed_before_delete"));
        Assert.Equal(
            "one_exact_immutable_object",
            RequiredString(mutation, "cleanup_authorization_scope"));
        Assert.Equal(
            "authorize_read_back_delete_record_per_object",
            RequiredString(mutation, "multi_object_cleanup_sequence"));
        Assert.Equal(
            ["automated", "operator_workflow", "api", "ui"],
            RequiredStrings(mutation, "cleanup_delete_paths"));
        Assert.Equal(
            "replay_promotion_blocked",
            RequiredString(mutation, "direct_delete_bypass_outcome"));
        Assert.Equal("no_further_mutation", RequiredString(mutation, "failure_outcome"));
    }

    [Fact]
    public void Snapshot_prose_defines_atomic_retention_and_an_exact_utc_boundary()
    {
        var text = Read("docs", "snapshot-retention.md");
        ValidateSnapshotContractText(text);
        var normalized = NormalizeWhitespace(text);

        Assert.Contains(
            "Bundle issuance appends and reads back its snapshot reference before the bundle is returned.",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "Every automated, operator-workflow, API or UI cleanup deletion path uses that same lock",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "At exactly 2,160 elapsed hours the release becomes deletion-eligible",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "A `no_eligible_release` result is final for that month",
            normalized,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Nightly releases are deleted after 30 days.")]
    [InlineData("Retention alone authorizes replay.")]
    [InlineData("Public reason_codes may contain free text.")]
    public void Snapshot_text_validator_rejects_contradictory_clauses(string contradiction)
    {
        var text = Read("docs", "snapshot-retention.md") + "\n" + contradiction + "\n";

        Assert.Throws<InvalidDataException>(() => ValidateSnapshotContractText(text));
    }

    [Fact]
    public void Machine_contract_makes_retention_necessary_but_not_sufficient_for_replay()
    {
        var replay = RequiredObject(Contract(), "replay_acceptance");

        Assert.True(RequiredBoolean(replay, "retention_is_necessary_not_sufficient"));
        Assert.Equal(
            [
                "canonical_plan_recomputation",
                "canonical_receipt_recomputation",
                "exact_tool_source_binding",
                "path_specific_schema_validation",
                "bounded_streaming_before_allocation",
                "strict_transport_validation",
                "static_pin_cross_checks",
                "practical_encoded_query_leak_detection",
                "controlled_malformed_set_rejection",
                "fresh_validation_bounded_age",
                "fresh_validation_bounded_duration",
                "not_assessed_no_specific_checks_status",
                "not_executed_as_contracted_status",
                "independent_live_probe_repeated"
            ],
            RequiredStrings(replay, "required_gates"));
        Assert.Equal("match_counts_only", RequiredString(replay, "privacy_canary_output"));
    }

    [Fact]
    public void Machine_contract_closes_public_inventory_reason_to_derived_codes()
    {
        var inventory = RequiredObject(Contract(), "public_inventory");

        Assert.Equal("reason_codes", RequiredString(inventory, "reason_field"));
        Assert.False(RequiredBoolean(inventory, "free_text_allowed"));
        Assert.Equal(
            [
                "evidence_bundle_reference",
                "monthly_keeper",
                "nightly_window",
                "current_production",
                "current_rollback"
            ],
            RequiredStrings(inventory, "reason_enum"));
        Assert.Equal(
            [
                "query_text",
                "request_body",
                "ip_address",
                "user_agent",
                "referrer",
                "cookies",
                "authorization",
                "bundle_narrative",
                "bundle_identifier"
            ],
            RequiredStrings(inventory, "forbidden_public_fields"));
    }

    [Fact]
    public void Machine_contract_rejects_unknown_or_alias_fields()
    {
        var contract = Contract();
        var revalidation = RequiredObject(contract, "revalidation");
        var retention = RequiredObject(contract, "retention");

        AssertProperties(contract,
            "schema", "status", "enforcement", "implementation_binding", "revalidation",
            "retention", "replay_acceptance", "public_inventory");
        AssertProperties(RequiredObject(contract, "implementation_binding"),
            "consumer_validation", "unknown_field_outcome", "missing_field_outcome",
            "unknown_value_outcome", "missing_live_read_back_outcome");
        AssertProperties(revalidation,
            "cadences", "http", "run_identity", "completion_requires", "absence_advances_on");
        AssertProperties(RequiredObject(revalidation, "cadences"),
            "nightly", "weekly", "monthly");
        AssertProperties(RequiredObject(revalidation, "http"),
            "method", "forbidden_methods", "validator_order", "missing_validator_outcome",
            "not_modified_outcome");
        AssertProperties(RequiredObject(revalidation, "run_identity"),
            "algorithm", "fields", "slot_formats", "scope_frozen_for_slot",
            "retry_must_reuse_identity", "excluded_fields");
        AssertProperties(
            RequiredObject(RequiredObject(revalidation, "run_identity"), "slot_formats"),
            "nightly", "weekly", "monthly");
        AssertProperties(retention, "classes", "clock", "monthly_selection", "mutation_protocol");
        AssertProperties(RequiredObject(retention, "classes"),
            "evidence_bundle_release", "nightly_release", "monthly_keeper");
        AssertProperties(RequiredObject(retention, "clock"),
            "source", "deletion_now_source", "clock_failure_outcome", "format",
            "caller_timestamp_allowed", "monotonic_against_ledger_head",
            "nightly_duration_hours", "delete_boundary");
        AssertProperties(RequiredObject(retention, "monthly_selection"),
            "eligibility_requires", "selection_order", "no_eligible_release_is_final",
            "selection_replaceable");
        AssertProperties(RequiredObject(retention, "mutation_protocol"),
            "cooperative_lock", "lock_users", "fencing_token_required", "ledger_head_cas_required",
            "bundle_reference_committed_before_return", "cleanup_authorization_committed_before_delete",
            "cleanup_authorization_scope", "multi_object_cleanup_sequence", "cleanup_delete_paths",
            "direct_delete_bypass_outcome", "failure_outcome");
        AssertProperties(RequiredObject(contract, "replay_acceptance"),
            "retention_is_necessary_not_sufficient", "required_gates", "privacy_canary_output");
        AssertProperties(RequiredObject(contract, "public_inventory"),
            "reason_field", "free_text_allowed", "reason_enum", "forbidden_public_fields");
    }

    [Fact]
    public void Snapshot_prose_publishes_the_separate_replay_and_privacy_gates()
    {
        var text = NormalizeWhitespace(Read("docs", "snapshot-retention.md"));

        Assert.Contains(
            "Retention is necessary but not sufficient for a replay claim.",
            text,
            StringComparison.Ordinal);
        Assert.Contains("canonical plan and receipt recomputation", text, StringComparison.Ordinal);
        Assert.Contains("bounded streaming before allocation", text, StringComparison.Ordinal);
        Assert.Contains("practical encoded-query leak detection", text, StringComparison.Ordinal);
        Assert.Contains("at least one live probe repeated independently", text, StringComparison.Ordinal);
        Assert.Contains(
            "Public `reason_codes` is a closed, derived-only enum and never free text.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "It never exposes a bundle identifier, bundle narrative, query text, request body, IP address, user-agent, referrer, cookie or authorization value.",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Public_entry_points_link_both_truth_contracts()
    {
        var readme = Read("README.md");
        var operations = Read("docs", "operations-retention.md");

        Assert.Contains("[Corpus revalidation](docs/corpus-revalidation.md)", readme,
            StringComparison.Ordinal);
        Assert.Contains("[Snapshot retention](docs/snapshot-retention.md)", readme,
            StringComparison.Ordinal);
        Assert.Contains("[corpus revalidation](corpus-revalidation.md)", operations,
            StringComparison.Ordinal);
        Assert.Contains("[snapshot retention and replay](snapshot-retention.md)", operations,
            StringComparison.Ordinal);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Golden.RepositoryRoot(), .. path]));

    private static JsonObject Contract() =>
        JsonNode.Parse(Read("docs", "truth-contract-v3.json"))?.AsObject()
        ?? throw new InvalidDataException("truth contract is empty");

    private static JsonObject RequiredObject(JsonObject parent, string name) =>
        parent[name]?.AsObject() ?? throw new InvalidDataException($"missing object: {name}");

    private static string RequiredString(JsonObject parent, string name) =>
        parent[name]?.GetValue<string>() ?? throw new InvalidDataException($"missing string: {name}");

    private static bool RequiredBoolean(JsonObject parent, string name) =>
        parent[name]?.GetValue<bool>() ?? throw new InvalidDataException($"missing boolean: {name}");

    private static int RequiredInt32(JsonObject parent, string name) =>
        parent[name]?.GetValue<int>() ?? throw new InvalidDataException($"missing integer: {name}");

    private static string[] RequiredStrings(JsonObject parent, string name) =>
        parent[name]?.AsArray().Select(value => value?.GetValue<string>()
            ?? throw new InvalidDataException($"null item: {name}")).ToArray()
        ?? throw new InvalidDataException($"missing array: {name}");

    private static void AssertProperties(JsonObject value, params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            value.Select(property => property.Key).Order(StringComparer.Ordinal));

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"\s+", " ");

    private static void ValidateRevalidationContractText(string text)
    {
        var normalized = NormalizeWhitespace(text);
        if (!normalized.Contains(
                "If neither validator exists, Lex performs an unconditional GET; it never substitutes HEAD.",
                StringComparison.Ordinal)
            || !normalized.Contains(
                "The scope manifest is frozen for that slot, and every retry must reuse the same identity.",
                StringComparison.Ordinal)
            || !normalized.Contains(
                "A nightly run is incomplete unless feed processing and the open and future-state enumeration both complete.",
                StringComparison.Ordinal))
            throw new InvalidDataException("revalidation contract clause missing");

        string[] contradictions =
        [
            "When validators are absent, Lex uses HEAD.",
            "A retry may mint a new run identity.",
            "A nightly run may complete after feed failure."
        ];
        if (contradictions.Any(clause => normalized.Contains(clause, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("contradictory revalidation clause");
    }

    private static void ValidateSnapshotContractText(string text)
    {
        var dayValues = Regex.Matches(text, @"\b(?<days>\d[\d,]*)[ -]days?\b",
                RegexOptions.IgnoreCase)
            .Select(match => int.Parse(match.Groups["days"].Value.Replace(",", "")))
            .Distinct()
            .ToArray();

        if (!dayValues.SequenceEqual([90]))
            throw new InvalidDataException($"conflicting retention days: {string.Join(",", dayValues)}");

        var normalized = NormalizeWhitespace(text);
        string[] contradictions =
        [
            "Retention alone authorizes replay.",
            "Public reason_codes may contain free text."
        ];
        if (contradictions.Any(clause => normalized.Contains(clause, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("contradictory snapshot clause");
    }
}
