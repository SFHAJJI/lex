import assert from "node:assert/strict";
import test from "node:test";
import {
  clearedSearchResults, everyPublisherRefused, LIMITATION_CAP, LIMITATION_EXPLANATION,
  LIMITATION_STATUS, limitationsFromEffect, limitationsFromEnvelopes, MIXED_ZERO_SENTENCES,
  partitionGovernedResponse, searchAbsenceState, searchEmptyPresentation,
  searchResultsFromError, searchResultsFromResponse, validateLimitation,
} from "./limitations.ts";

const refused = (publisher: string, filters: string[]) => ({
  envelope: { status: LIMITATION_STATUS, publisher, jurisdiction: publisher === "eu-eurlex" ? "eu" : "lu" },
  unsupported_filters: filters,
});
const supported = (publisher: string, rows: number) => ({
  envelope: { status: "ok", publisher },
  changes: Array.from({ length: rows }, (_, index) => ({ work: `w${index}` })),
});

test("mixed search response keeps one limitation per refusing publisher", () => {
  const out = limitationsFromEnvelopes("search", [
    supported("lu-legilux", 3), refused("eu-eurlex", ["domain"]),
  ]);
  assert.equal(out.length, 1);
  assert.equal(out[0].publisher, "eu-eurlex");
  assert.deepEqual(out[0].unsupported_filters, ["domain"]);
  assert.equal(out[0].tool, "search");
});

test("mixed period response derives the limitation and never touches supported rows", () => {
  const envs = [refused("lu-legilux", ["binding_status", "act_form"]), supported("eu-eurlex", 2)];
  const out = limitationsFromEnvelopes("changes_in_period", envs);
  assert.equal(out.length, 1);
  assert.deepEqual(out[0].unsupported_filters, ["act_form", "binding_status"]);
  // Derivation is read-only: the envelopes, including supported rows, are unchanged.
  assert.equal((envs[1] as { changes: unknown[] }).changes.length, 2);
});

test("mixed in-force response derives the limitation with the right tool name", () => {
  const out = limitationsFromEnvelopes("in_force_on", [
    supported("eu-eurlex", 1), refused("lu-legilux", ["hierarchy"]),
  ]);
  assert.equal(out.length, 1);
  assert.equal(out[0].tool, "in_force_on");
});

test("an all-refused call is detected so the caller keeps its full typed gap", () => {
  assert.equal(everyPublisherRefused(
    [refused("lu-legilux", ["domain"]), refused("eu-eurlex", ["domain"])]), true);
  assert.equal(everyPublisherRefused(
    [refused("lu-legilux", ["domain"]), supported("eu-eurlex", 0)]), false);
  assert.equal(everyPublisherRefused([]), false);
});

test("malformed limitation objects are ignored and never render", () => {
  assert.equal(validateLimitation(null), null);
  assert.equal(validateLimitation("x"), null);
  assert.equal(validateLimitation({ status: "ok", tool: "search", unsupported_filters: ["domain"] }), null);
  assert.equal(validateLimitation({ status: LIMITATION_STATUS, tool: "drop_tables", unsupported_filters: ["domain"] }), null);
  assert.equal(validateLimitation({ status: LIMITATION_STATUS, tool: "search", unsupported_filters: [] }), null);
  assert.equal(validateLimitation({ status: LIMITATION_STATUS, tool: "search", unsupported_filters: ["made_up"] }), null);
  assert.equal(validateLimitation({ status: LIMITATION_STATUS, tool: "search", unsupported_filters: "domain" }), null);
  // Unbounded identifiers are dropped, the entry survives with the field absent.
  const oversized = validateLimitation({
    status: LIMITATION_STATUS, tool: "search", publisher: "x".repeat(65),
    unsupported_filters: ["domain"],
  });
  assert.ok(oversized);
  assert.equal(oversized!.publisher, undefined);
});

test("more than eight refusal envelopes are capped, never summarized into prose", () => {
  const envs = Array.from({ length: 12 }, (_, index) => refused(`p${index}`, ["domain"]));
  assert.equal(limitationsFromEnvelopes("search", envs).length, LIMITATION_CAP);
  const effect = Array.from({ length: 12 }, (_, index) => ({
    status: LIMITATION_STATUS, tool: "search", publisher: `p${index}`,
    unsupported_filters: ["domain"],
  }));
  assert.equal(limitationsFromEffect(effect).length, LIMITATION_CAP);
});

test("the assistant effect field validates fail closed entry by entry", () => {
  const out = limitationsFromEffect([
    { status: LIMITATION_STATUS, tool: "search", unsupported_filters: ["domain"] },
    { status: LIMITATION_STATUS, tool: "search", unsupported_filters: ["nope"] },
    "garbage",
    null,
  ]);
  assert.equal(out.length, 1);
  assert.equal(limitationsFromEffect(undefined).length, 0);
  assert.equal(limitationsFromEffect("not-a-list").length, 0);
});

test("the fixed explanation carries no interpolation and no query placeholder", () => {
  assert.ok(LIMITATION_EXPLANATION.length > 40);
  assert.ok(!LIMITATION_EXPLANATION.includes("{"));
  assert.ok(!LIMITATION_EXPLANATION.includes("%s"));
});

test("an ungoverned tool derives nothing even from a refusing envelope", () => {
  assert.equal(limitationsFromEnvelopes("as_of", [refused("lu-legilux", ["domain"])]).length, 0);
});

test("two separately allocated identical limitations render once (O3 dedup)", () => {
  const one = () => ({
    status: LIMITATION_STATUS, tool: "search", publisher: "lu-legilux",
    jurisdiction: "lu", unsupported_filters: ["domain", "act_form"],
  });
  const out = limitationsFromEffect([one(), one()]);
  assert.equal(out.length, 1);
  // Filter order is part of normalization, not identity: reversed order is the same object.
  const reversed = { ...one(), unsupported_filters: ["act_form", "domain"] };
  assert.equal(limitationsFromEffect([one(), reversed]).length, 1);
  // The envelope route dedups too: the same publisher refusing twice is one limitation.
  const envs = [
    { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux", jurisdiction: "lu" },
      unsupported_filters: ["domain"] },
    { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux", jurisdiction: "lu" },
      unsupported_filters: ["domain"] },
  ];
  assert.equal(limitationsFromEnvelopes("search", envs).length, 1);
  // Distinct publishers stay distinct.
  const distinct = [
    { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" }, unsupported_filters: ["domain"] },
    { envelope: { status: LIMITATION_STATUS, publisher: "eu-eurlex" }, unsupported_filters: ["domain"] },
  ];
  assert.equal(limitationsFromEnvelopes("search", distinct).length, 2);
});

test("empty-state truth scope: all-refused, mixed, and corpus-wide are distinct (O1)", () => {
  const refusedEnv = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"] };
  const okEnv = { envelope: { status: "ok", publisher: "eu-eurlex" }, hits: [] };
  const p = (envs: unknown[]) => partitionGovernedResponse("search", envs);
  // All refused, zero hits: coverage, never absence.
  assert.equal(searchAbsenceState(p([refusedEnv, refusedEnv]), 0), "all_refused");
  // One publisher ran and found nothing while another refused: mixed state.
  assert.equal(searchAbsenceState(p([refusedEnv, okEnv]), 0), "mixed_no_match");
  // Only when every selected publisher ran is the corpus-wide sentence true.
  assert.equal(searchAbsenceState(p([okEnv, okEnv]), 0), "no_match");
  // Hits from publishers that ran render results regardless of refusals beside them.
  assert.equal(searchAbsenceState(p([refusedEnv, okEnv]), 3), "has_results");
  // No envelopes at all is not an all-refused claim.
  assert.equal(searchAbsenceState(p([]), 0), "no_match");
  // Round 3, O1: a refused envelope's rows never produce has_results. The partition strips
  // the contradictory envelope before any row can be counted, so the all-refused state wins
  // even when the refusal smuggles a row.
  const contradictory = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"],
    hits: [{ lex_id: "lu-legilux:contradiction", title: "MUST NOT RENDER" }] };
  const part = p([contradictory]);
  assert.equal(part.ran.length, 0);
  assert.equal(part.allRefused, true);
  assert.equal(searchAbsenceState(part, part.ran.length), "all_refused");
});

test("row authority: refused envelopes contribute no rows on any path (round 3, O1)", () => {
  const contradictorySearch = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"], hits: [{ lex_id: "x", title: "MUST NOT RENDER" }] };
  const contradictoryPeriod = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"], changes: [{ work: "refused-row", versions_in_period: 1 }] };
  const contradictoryForce = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"], works: [{ work: "refused-row" }] };
  // This mirrors the production projection exactly: rows come from partition.ran only.
  for (const [tool, env, key] of [
    ["search", contradictorySearch, "hits"],
    ["changes_in_period", contradictoryPeriod, "changes"],
    ["in_force_on", contradictoryForce, "works"],
  ] as const) {
    const part = partitionGovernedResponse(tool, [env]);
    const rows = (part.ran as Record<string, unknown>[]).flatMap(
      (e) => (e?.[key] as unknown[]) ?? []);
    assert.equal(rows.length, 0, `${tool}: refused rows must not project`);
    assert.equal(part.limitations.length, 1, `${tool}: the refusal must remain typed`);
    assert.equal(part.allRefused, true);
  }
  // A mixed call still projects the supported publisher's rows, and only those.
  const okPeriod = { envelope: { status: "ok", publisher: "eu-eurlex" },
    changes: [{ work: "real-row", versions_in_period: 2 }] };
  const mixed = partitionGovernedResponse("changes_in_period", [contradictoryPeriod, okPeriod]);
  const mixedRows = (mixed.ran as Record<string, unknown>[]).flatMap(
    (e) => (e?.changes as unknown[]) ?? []);
  assert.equal(mixedRows.length, 1);
  assert.equal((mixedRows[0] as Record<string, unknown>).work, "real-row");
});

test("mixed zero on the direct paths never claims whole-scope absence (round 3, O2)", () => {
  const refused = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"] };
  const okEmptyPeriod = { envelope: { status: "ok", publisher: "eu-eurlex" }, changes: [] };
  const okEmptyForce = { envelope: { status: "ok", publisher: "eu-eurlex" }, works: [] };
  // The production branch decision: rows empty, not all refused, one refusal -> mixed gap
  // with the operation's scoped sentence, never the whole-scope claim.
  for (const [tool, envs] of [
    ["changes_in_period", [okEmptyPeriod, refused]],
    ["in_force_on", [okEmptyForce, refused]],
  ] as const) {
    const part = partitionGovernedResponse(tool, [...envs]);
    assert.equal(part.allRefused, false);
    assert.equal(part.anyRefused, true);
    const sentence = MIXED_ZERO_SENTENCES[tool];
    assert.ok(sentence.includes("publishers that could apply these filters"));
    assert.ok(!sentence.includes("Nothing changed"));
    assert.ok(!sentence.includes("No publisher state covers"));
  }
  // Both publishers ran and found nothing: the whole-scope sentences are then true and the
  // mixed sentence must NOT be selected.
  const clean = partitionGovernedResponse("changes_in_period", [okEmptyPeriod, okEmptyPeriod]);
  assert.equal(clean.anyRefused, false);
});

test("the production presentation maps each empty state to its scoped sentence (O1)", () => {
  // This IS the render decision: Search.tsx passes results.absence into this presenter and
  // prints the returned sentence, so a wrong mapping here is the production surface lying.
  assert.equal(searchEmptyPresentation("no_match").sentence,
    "Nothing in the corpus matches that.");
  assert.equal(searchEmptyPresentation("mixed_no_match").sentence,
    "No match was returned by the publishers that could apply these filters.");
  assert.equal(searchEmptyPresentation("all_refused").sentence,
    "No selected publisher ran this query.");
  // The corpus-wide sentence must be unreachable from the two refusal-bearing states.
  assert.notEqual(searchEmptyPresentation("mixed_no_match").sentence,
    searchEmptyPresentation("no_match").sentence);
  assert.notEqual(searchEmptyPresentation("all_refused").sentence,
    searchEmptyPresentation("no_match").sentence);
});

test("state transitions: a refused response then a cleared query leaves nothing behind (O2)", () => {
  const refusedEnv = { envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["domain"] };
  // The exact production wiring: Search.tsx builds its state through these transitions.
  const after = searchResultsFromResponse(
    partitionGovernedResponse("search", [refusedEnv]), 0,
    { works: [], articles: [], expansions: [], modeUnavailable: undefined });
  assert.equal(after.limitations.length, 1);
  assert.equal(after.absence, "all_refused");
  const cleared = clearedSearchResults();
  assert.deepEqual(cleared.limitations, []);
  assert.equal(cleared.absence, "no_match");
  // The error transition clears result state too, carrying only its sentence.
  const failed = searchResultsFromError("Search could not be reached. Try again.");
  assert.deepEqual(failed.limitations, []);
  assert.equal(failed.error, "Search could not be reached. Try again.");
  assert.equal(failed.absence, "no_match");
});

test("the cleared search tuple clears the limitation state (O2)", () => {
  const cleared = clearedSearchResults();
  assert.deepEqual(cleared.limitations, []);
  assert.deepEqual(cleared.works, []);
  assert.deepEqual(cleared.articles, []);
  assert.equal(cleared.error, undefined);
  assert.equal(cleared.modeUnavailable, undefined);
  assert.deepEqual(cleared.expansions, []);
  // The tuple is the union of every result-bearing key the search surface sets; a key added
  // to the surface without joining this tuple is the exact stale-state defect of review O2.
  assert.deepEqual(Object.keys(cleared).sort(),
    ["absence", "articles", "error", "expansions", "limitations", "modeUnavailable", "works"]);
});
