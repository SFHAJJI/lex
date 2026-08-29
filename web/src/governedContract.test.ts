import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { classifyEnvelope } from "./limitations.ts";

/**
 * Binds the browser classifier to the producer-bound status contract.
 *
 * The contract is generated against McpCore by GovernedStatusContractTests, which fails when a
 * governed tool gains a status with no contract entry. This file closes the other half: for
 * every (tool, status) the producer can emit, the shipped classifier must agree with the
 * contract's classification. A status the producer starts emitting therefore breaks the C#
 * canary, and a client that disagrees about an existing one breaks here.
 *
 * Written after four review rounds in which every round found a producer status or shape the
 * author had not modelled, and after the contract's first run caught three phantom search
 * statuses the author had previously called verified against the producer.
 */

interface Contract {
  tools: Record<string, Record<string, string>>;
  shape_rules: Record<string, {
    rows_field: string;
    row_required_fields: string[];
    required_counts?: string[];
    ambiguity_field?: string;
  }>;
}

const contract: Contract = JSON.parse(
  readFileSync(new URL("../../tests/Lex.Tests/governed-status-contract.json", import.meta.url),
    "utf8"));

/** How a contract classification must appear at the classifier's own boundary. */
const EXPECTED_KIND: Record<string, string> = {
  ran: "ran",
  capability_refused: "refused",
  mode_unavailable: "mode_unavailable",
  terminal_refusal: "terminal_refusal",
  invalid: "invalid",
};

/**
 * The population the producer publishes beside this (tool, status), if any.
 *
 * `McpCore.SearchPopulation` attaches one to all three search paths and derives its two booleans
 * and its basis from which path it is; the other two tools publish one only where they ran, and
 * return `UnsupportedFilterResult` unchanged on a refusal.
 */
function populationFor(tool: string, status: string): Record<string, unknown> | undefined {
  if (tool === "search") {
    const scopeFiltersApplied = status !== "filter_not_supported_by_index";
    return {
      basis: scopeFiltersApplied
        ? "selected_metadata_scope"
        : "mounted_scope_before_unsupported_filters",
      works_in_scope: 1250,
      scope_filters_applied: scopeFiltersApplied,
      query_ran: status === "ok",
      known_exclusions: [],
    };
  }
  if (status === "filter_not_supported_by_index") return undefined;
  return tool === "changes_in_period"
    ? {
      basis: "distinct non-withdrawn works in the selected publisher and legal metadata scope",
      works_in_scope: 1250,
      known_exclusions: [],
    }
    : { basis: "versioned works only", works_covered: 1250, known_exclusions: [] };
}

/** A minimal envelope that satisfies this tool's declared shape for a given status. */
function envelopeFor(tool: string, status: string): Record<string, unknown> {
  const rule = contract.shape_rules[tool]!;
  const row: Record<string, unknown> = {};
  for (const field of rule.row_required_fields) {
    // "work|lex_id" means either identifies the row; supply the first alternative.
    row[field.split("|")[0]!] = `${tool}-row-1`;
  }
  if (tool === "search") row.lex_id = "lu-legilux:w1:2024-01-01";

  const empty = status === "no_result" || status === "no_changes_in_period";
  const ambiguous = status === "ambiguous_version";
  const rows = empty || ambiguous ? [] : [row];

  const entry: Record<string, unknown> = {
    envelope: { publisher: "lu-legilux", status },
    [rule.rows_field]: rows,
  };
  // The producer publishes a population on every path it can classify, and an unreadable
  // required scope now invalidates the whole claim rather than only the scope. A fixture without
  // one would classify invalid for every status and this file would pass for the wrong reason.
  const population = populationFor(tool, status);
  if (population !== undefined) entry.population = population;
  for (const count of rule.required_counts ?? []) entry[count] = rows.length;
  if (tool === "changes_in_period") entry.new_versions = rows.length;
  if (tool === "search") entry.retrieval_mode = "keyword";
  if (ambiguous && rule.ambiguity_field) {
    entry[rule.ambiguity_field] = [row];
    if (rule.required_counts) entry[rule.required_counts[0]!] = 1;
  }
  // A capability refusal carries its typed limitation and no rows.
  if (status === "filter_not_supported_by_index") {
    entry[rule.rows_field] = [];
    entry.unsupported_filters = ["domain"];
    for (const count of rule.required_counts ?? []) entry[count] = 0;
  }
  return entry;
}

test("the shipped classifier agrees with the producer contract for every status", () => {
  let checked = 0;
  for (const [tool, statuses] of Object.entries(contract.tools)) {
    for (const [status, classification] of Object.entries(statuses)) {
      const expected = EXPECTED_KIND[classification];
      assert.ok(expected, `contract uses unknown classification ${classification}`);
      const actual = classifyEnvelope(tool, envelopeFor(tool, status)).kind;
      assert.equal(actual, expected,
        `${tool} + ${status}: contract says ${classification} (${expected}), classifier said ${actual}`);
      checked += 1;
    }
  }
  assert.ok(checked >= 9, `expected the full governed matrix, checked only ${checked}`);
});

test("a status outside the contract is never admitted as having run", () => {
  // The producer cannot emit these for these tools. If one appears, the response did not come
  // from the producer contract and must authorize neither rows nor absence claims.
  for (const [tool, statuses] of Object.entries(contract.tools)) {
    for (const foreign of ["no_result", "ok_", "OK", "unknown_work", "made_up", ""]) {
      if (foreign in statuses) continue;
      assert.equal(classifyEnvelope(tool, envelopeFor(tool, foreign)).kind, "invalid",
        `${tool} admitted foreign status ${foreign}`);
    }
  }
});

test("the contract covers every tool the classifier governs", () => {
  // A governed tool missing from the contract would silently classify as invalid forever.
  for (const tool of ["search", "changes_in_period", "in_force_on"]) {
    assert.ok(tool in contract.tools, `contract omits governed tool ${tool}`);
    assert.ok(tool in contract.shape_rules, `contract omits shape rules for ${tool}`);
  }
});
