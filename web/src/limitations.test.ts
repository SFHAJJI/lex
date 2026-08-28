import assert from "node:assert/strict";
import test from "node:test";
import {
  classifyEnvelope, clearedSearchResults, gapBadgeStatus, INCOMPLETE_RESPONSE_SENTENCE,
  LIMITATION_CAP, NO_CORPUS_SENTENCE, NO_CORPUS_STATUS,
  LIMITATION_EXPLANATION, LIMITATION_STATUS, limitationsForTool, limitationsFromEffect,
  MIXED_ZERO_SENTENCES, partitionGovernedResponse, projectGovernedEmptiness,
  projectSearchResponse, searchAbsenceState, searchEmptyPresentation, searchResultsFromError,
  validateLimitation,
} from "./limitations.ts";

const refused = (publisher: string, filters: string[]) => ({
  envelope: {
    status: LIMITATION_STATUS, publisher,
    jurisdiction: publisher === "eu-eurlex" ? "eu" : "lu",
  },
  unsupported_filters: filters,
});

/** A coherent changes_in_period success: rows and counts agree. */
const changesOk = (publisher: string, rows: number) => ({
  envelope: { status: "ok", publisher },
  changes: Array.from({ length: rows }, (_, index) => ({ work: `w${index}` })),
  works_changed: rows,
  new_versions: rows,
});

/** A coherent search success. */
const searchOk = (publisher: string, hits: number, extra: Record<string, unknown> = {}) => ({
  envelope: { status: "ok", publisher },
  hits: Array.from({ length: hits }, (_, index) => ({
    lex_id: `${publisher}:w${index}:2024-01-01`, title: `Work ${index}`,
    valid_from: "2024-01-01",
  })),
  ...extra,
});

/** A coherent in_force_on success. */
const inForceOk = (publisher: string, rows: number) => ({
  envelope: { status: "ok", publisher },
  works: Array.from({ length: rows }, (_, index) => ({
    work: `w${index}`, title: `Work ${index}`, valid_from: "2024-01-01",
  })),
  total_works_in_force: rows,
});

/** The production search projection, with the component's own mapping shape. */
const projectSearch = (raw: unknown) =>
  projectSearchResponse<{ work: string }, { work: string }>(raw, (ran) => {
    const hits = (ran as any[]).flatMap((entry) => entry?.hits ?? []);
    const works = [...new Map(hits.map((hit: any) => {
      const work = String(hit.lex_id ?? "").split(":").slice(0, 2).join(":");
      return [work, { work }];
    })).values()];
    return { works, articles: [], ranHitCount: hits.length };
  });

// ---------------------------------------------------------------------------
// Closed status classification (round 4, O1)
// ---------------------------------------------------------------------------

test("only an allowed terminal success status with a valid shape enters ran", () => {
  const cases: [string, unknown, string][] = [
    ["search", searchOk("lu-legilux", 2), "ran"],
    ["search", { envelope: { status: "ok", publisher: "p" } }, "invalid"],
    ["search", { envelope: { publisher: "p" }, hits: [] }, "invalid"],
    ["search", { envelope: { status: null }, hits: [] }, "invalid"],
    ["search", { envelope: { status: "OK" }, hits: [] }, "invalid"],
    ["search", { envelope: { status: "okay" }, hits: [] }, "invalid"],
    ["search", { envelope: { status: "no_result" }, hits: [] }, "invalid"],
    ["search", { hits: [] }, "invalid"],
    ["search", null, "invalid"],
    ["search", "ok", "invalid"],
    ["search", { envelope: { status: "retrieval_mode_unavailable", publisher: "p" } },
      "mode_unavailable"],
    ["changes_in_period", changesOk("lu-legilux", 1), "ran"],
    ["changes_in_period",
      { envelope: { status: "no_changes_in_period" }, changes: [], works_changed: 0 }, "ran"],
    ["changes_in_period", { envelope: { status: "ok" }, changes: [] }, "invalid"],
    ["in_force_on", inForceOk("lu-legilux", 1), "ran"],
    ["in_force_on",
      { envelope: { status: "no_result" }, works: [], total_works_in_force: 0 }, "ran"],
    ["in_force_on", { envelope: { status: "unknown_work" }, works: [] }, "invalid"],
  ];
  for (const [tool, value, kind] of cases) {
    assert.equal(classifyEnvelope(tool, value).kind, kind,
      `${tool} ${JSON.stringify(value)?.slice(0, 60)}`);
  }
});

test("a refusal without its typed limitation is invalid, not evidence-free", () => {
  assert.equal(classifyEnvelope("search", {
    envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
  }).kind, "invalid");
  assert.equal(classifyEnvelope("search", {
    envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["not_a_governed_filter"],
  }).kind, "invalid");
  assert.equal(classifyEnvelope("search", refused("lu-legilux", ["domain"])).kind, "refused");
});

test("an empty response never becomes a successful no-match response", () => {
  assert.equal(searchAbsenceState(partitionGovernedResponse("search", []), 0),
    "incomplete_response");
  assert.equal(projectSearch([]).absence, "incomplete_response");
  assert.equal(projectGovernedEmptiness("changes_in_period", [], 0).empty,
    "incomplete_response");
  assert.equal(projectGovernedEmptiness("in_force_on", [], 0).empty, "incomplete_response");
});

test("an unknown or missing status authorizes neither rows nor absence claims", () => {
  const sneaky = {
    envelope: { publisher: "lu-legilux" },
    hits: [{ lex_id: "lu-legilux:w1:2024-01-01" }],
  };
  const projected = projectSearch([sneaky]);
  assert.deepEqual(projected.works, [], "no rows from an unclassifiable envelope");
  assert.equal(projected.absence, "incomplete_response");
  assert.notEqual(projected.absence, "no_match");
});

// ---------------------------------------------------------------------------
// Count and row coherence (round 4, O2)
// ---------------------------------------------------------------------------

test("only impossible count/row directions are contradictions", () => {
  // Zero count beside retained rows is internally contradictory evidence.
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "ok" }, changes: [{ work: "w1" }], works_changed: 0, new_versions: 0,
  }).kind, "invalid");
  // Rows exceeding their count cannot both be true.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok" }, works: [{ work: "a" }, { work: "b" }],
    total_works_in_force: 1,
  }).kind, "invalid");
  // Rows plus ambiguity units may not exceed the total either.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ambiguous_version" }, works: [{ work: "a" }],
    ambiguous_works: [{ work: "b" }, { work: "c" }], total_works_in_force: 2,
  }).kind, "invalid");
  // Non-finite, negative, fractional and wrong-typed counts are all invalid.
  for (const total of [Number.NaN, Number.POSITIVE_INFINITY, -1, 1.5, "3", {}, []]) {
    assert.equal(classifyEnvelope("in_force_on", {
      envelope: { status: "ok" }, works: [{ work: "a" }], total_works_in_force: total,
    }).kind, "invalid", `total ${String(total)}`);
  }
  // A no_changes_in_period envelope must actually be empty.
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "no_changes_in_period" }, changes: [{ work: "w" }], works_changed: 1,
  }).kind, "invalid");
});

test("a paged publisher with zero rows beside a positive count is legitimate", () => {
  // Self-attack finding, round 6: the producer shares ONE remaining limit across publishers
  // (in_force_on) and slices ONE globally merged page (changes_in_period) while each
  // publisher still reports its own full total. Round 5 called these contradictions and
  // silently dropped whole publishers out of the headline counts.
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "ok", publisher: "eu-eurlex" },
    changes: [], works_changed: 7, new_versions: 7,
  }).kind, "ran");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok", publisher: "eu-eurlex" },
    works: [], total_works_in_force: 412,
  }).kind, "ran");
  // And the count still reaches the caller rather than being discarded.
  const partition = partitionGovernedResponse("in_force_on", [
    { envelope: { status: "ok", publisher: "eu-eurlex" }, works: [],
      total_works_in_force: 412 },
    inForceOk("lu-legilux", 2),
  ]);
  assert.equal(partition.ran.length, 2);
  assert.equal(partition.invalidCount, 0);
});

test("an all-ambiguity in-force page is content, never an absence claim", () => {
  const ambiguousOnly = {
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    works: [],
    ambiguous_works: [{ work: "w1" }, { work: "w2" }],
    total_works_in_force: 2,
  };
  assert.equal(classifyEnvelope("in_force_on", ambiguousOnly).kind, "ran");
  const decision = projectGovernedEmptiness("in_force_on", [ambiguousOnly], 0);
  assert.equal(decision.partition.ambiguityUnits, 2);
  assert.equal(decision.empty, null,
    "ambiguity units are held content; claiming nothing covers the date would be false");
});

test("no_corpus_mounted is a terminal refusal, not a malformed response", () => {
  // The producer returns a TOP-LEVEL status with no envelope for every tool. Round 5
  // classified it invalid and told the reader to retry a request that can never succeed.
  const noCorpus = { status: NO_CORPUS_STATUS, tool_called: "search" };
  assert.equal(classifyEnvelope("search", noCorpus).kind, "no_corpus");
  assert.equal(classifyEnvelope("in_force_on", noCorpus).kind, "no_corpus");
  const projected = projectSearch([noCorpus]);
  assert.equal(projected.absence, "no_corpus");
  assert.equal(searchEmptyPresentation("no_corpus").sentence, NO_CORPUS_SENTENCE);
  assert.ok(!NO_CORPUS_SENTENCE.toLowerCase().includes("try"),
    "retrying cannot help, so the copy never suggests it");
  assert.equal(projectGovernedEmptiness("in_force_on", [noCorpus], 0).empty, "no_corpus");
});

test("a contradictory envelope becomes incomplete, never results or absence", () => {
  const contradictory = {
    envelope: { status: "ok", publisher: "lu-legilux" },
    changes: [{ work: "w1" }], works_changed: 0, new_versions: 0,
  };
  const decision = projectGovernedEmptiness("changes_in_period", [contradictory], 0);
  assert.equal(decision.empty, "incomplete_response");
  assert.deepEqual(decision.partition.ran, []);
});

test("an unusable sibling is disclosed even when other rows render", () => {
  // Round 5 consulted invalidCount only on the empty path, so a partial answer presented
  // itself as the complete holding.
  const projected = projectSearch([
    searchOk("lu-legilux", 2),
    { envelope: { status: "made_up" }, hits: [] },
  ]);
  assert.equal(projected.works.length, 2, "the usable publisher's rows still render");
  assert.equal(projected.absence, "partial_results",
    "rows rendered, but the response was not complete and must say so");

  const governed = projectGovernedEmptiness("in_force_on", [
    inForceOk("lu-legilux", 2),
    { envelope: { status: "made_up" }, works: [] },
  ], 2);
  assert.equal(governed.empty, null);
  assert.equal(governed.partial, true);
});

test("a missing retrieval mode is not a refused filter", () => {
  // Round 5 folded mode-unavailable into allRefused, so a search where no publisher had the
  // hybrid mode rendered the filter-capability explanation, blaming a filter never refused.
  const modeOnly = projectSearch([
    { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } },
  ]);
  assert.equal(modeOnly.limitations.length, 0, "no capability limitation exists");
  assert.notEqual(modeOnly.absence, "all_refused",
    "all_refused selects the filter-refusal copy and no filter was refused");
  assert.ok(modeOnly.modeUnavailable?.includes("lu-legilux"));

  // A real capability refusal still reaches all_refused.
  assert.equal(projectSearch([refused("lu-legilux", ["domain"])]).absence, "all_refused");
});

// ---------------------------------------------------------------------------
// Retrieval disclosures ride the validated partition (round 4, O3)
// ---------------------------------------------------------------------------

test("a refused response cannot claim that meaning search ran", () => {
  const lying = {
    envelope: { status: LIMITATION_STATUS, publisher: "eu-eurlex" },
    unsupported_filters: ["domain"],
    retrieval_mode: "hybrid",
    query_expansions: ["invented"],
  };
  const projected = projectSearch([lying]);
  assert.equal(projected.modeUsed, undefined, "no mode claim from a refusal");
  assert.deepEqual(projected.expansions, [], "no expansion claim from a refusal");
  assert.equal(projected.absence, "all_refused");
  assert.equal(projected.limitations.length, 1);
});

test("an invalid envelope cannot claim a fallback was tried", () => {
  const projected = projectSearch([
    { envelope: { status: "made_up" }, hits: [], retrieval_mode: "keyword",
      query_expansions: ["ghost"] },
  ]);
  assert.equal(projected.modeUsed, undefined);
  assert.deepEqual(projected.expansions, []);
});

test("mode and expansions come from ran envelopes and are bounded", () => {
  const projected = projectSearch([
    searchOk("lu-legilux", 1, {
      retrieval_mode: "hybrid",
      query_expansions: ["travail", "x".repeat(200), 42, "travail"],
    }),
    refused("eu-eurlex", ["domain"]),
  ]);
  assert.equal(projected.modeUsed, "hybrid");
  assert.deepEqual(projected.expansions, ["travail"], "over-long and non-string dropped");
  assert.equal(projected.absence, "has_results");
});

test("actual mode lives in the state, so every transition clears it structurally", () => {
  const ran = projectSearch([searchOk("lu-legilux", 1, { retrieval_mode: "hybrid" })]);
  assert.equal(ran.modeUsed, "hybrid");
  assert.equal(clearedSearchResults().modeUsed, undefined, "cleared query clears the badge");
  assert.equal(searchResultsFromError("boom").modeUsed, undefined, "an error clears it too");
  // A later response with no ran envelope cannot retain the earlier hybrid badge.
  assert.equal(projectSearch([refused("lu-legilux", ["domain"])]).modeUsed, undefined);
});

test("the retrieval-mode notice names only the unavailable publishers", () => {
  const projected = projectSearch([
    { envelope: { status: "retrieval_mode_unavailable", publisher: "eu-eurlex" } },
    searchOk("lu-legilux", 1, { retrieval_mode: "keyword" }),
  ]);
  assert.ok(projected.modeUnavailable?.includes("eu-eurlex"));
  assert.equal(projected.modeUsed, "keyword");
  assert.equal(projected.absence, "has_results");
});

// ---------------------------------------------------------------------------
// Row authority and typed absence through the production seams (O4)
// ---------------------------------------------------------------------------

test("row authority: refused envelopes contribute no rows on any governed path", () => {
  const searchProjection = projectSearch([
    { ...refused("eu-eurlex", ["domain"]),
      hits: [{ lex_id: "eu-eurlex:smuggled:2024-01-01" }] },
    searchOk("lu-legilux", 1),
  ]);
  assert.equal(searchProjection.works.length, 1);
  assert.equal(searchProjection.works[0].work, "lu-legilux:w0");

  const changes = projectGovernedEmptiness("changes_in_period", [
    { ...refused("eu-eurlex", ["domain"]), changes: [{ work: "smuggled" }], works_changed: 1 },
  ], 0);
  assert.deepEqual(changes.partition.ran, []);
  assert.equal(changes.empty, "all_refused");

  const inForce = projectGovernedEmptiness("in_force_on", [
    { ...refused("lu-legilux", ["hierarchy"]), works: [{ work: "smuggled" }],
      total_works_in_force: 1 },
    inForceOk("eu-eurlex", 2),
  ], 2);
  assert.equal(inForce.partition.ran.length, 1);
  assert.equal(inForce.empty, null);
  assert.equal(inForce.partition.limitations.length, 1);
});

test("empty-state truth scope stays distinct across every governed path", () => {
  assert.equal(projectSearch([refused("lu-legilux", ["domain"])]).absence, "all_refused");
  assert.equal(projectSearch([
    refused("eu-eurlex", ["domain"]), searchOk("lu-legilux", 0),
  ]).absence, "mixed_no_match");
  assert.equal(projectSearch([searchOk("lu-legilux", 0)]).absence, "no_match");
  assert.equal(projectSearch([searchOk("lu-legilux", 2)]).absence, "has_results");

  assert.equal(projectGovernedEmptiness("changes_in_period",
    [refused("lu-legilux", ["domain"])], 0).empty, "all_refused");
  assert.equal(projectGovernedEmptiness("changes_in_period",
    [refused("eu-eurlex", ["domain"]), changesOk("lu-legilux", 0)], 0).empty,
    "mixed_no_match");
  assert.equal(projectGovernedEmptiness("changes_in_period",
    [changesOk("lu-legilux", 0)], 0).empty, "none_matched");
});

test("the production presentation maps each empty state to its scoped sentence", () => {
  assert.equal(searchEmptyPresentation("all_refused").sentence,
    "No selected publisher ran this query.");
  assert.equal(searchEmptyPresentation("mixed_no_match").sentence,
    MIXED_ZERO_SENTENCES.search);
  assert.equal(searchEmptyPresentation("no_match").sentence,
    "Nothing in the corpus matches that.");
  assert.equal(searchEmptyPresentation("incomplete_response").sentence,
    INCOMPLETE_RESPONSE_SENTENCE);
  // The incomplete sentence claims nothing about the corpus.
  assert.ok(!INCOMPLETE_RESPONSE_SENTENCE.includes("corpus"));
});

test("out-of-order and repeated transitions leave nothing stale", () => {
  const withEverything = projectSearch([
    searchOk("lu-legilux", 1, { retrieval_mode: "hybrid", query_expansions: ["travail"] }),
    refused("eu-eurlex", ["domain"]),
  ]);
  assert.equal(withEverything.limitations.length, 1);
  assert.equal(withEverything.expansions.length, 1);

  const cleared = clearedSearchResults() as unknown as Record<string, unknown>;
  for (const [key, value] of Object.entries({
    works: [], articles: [], error: undefined, modeUsed: undefined,
    modeUnavailable: undefined, expansions: [], limitations: [],
  })) {
    assert.deepEqual(cleared[key], value, key);
  }
  // Every key the response path can set is cleared by the shared tuple.
  assert.deepEqual(Object.keys(cleared).sort(), Object.keys(withEverything).sort());
});

// ---------------------------------------------------------------------------
// Client presentation states are not wire statuses (round 4, O5)
// ---------------------------------------------------------------------------

test("a client-only presentation state never wears the wire status badge", () => {
  assert.equal(gapBadgeStatus("mixed_no_match"), null);
  assert.equal(gapBadgeStatus("incomplete_response"), null);
  // "error" is minted by the transport-failure branches, not by any publisher; the status
  // register is explicit that transport failures are represented separately.
  assert.equal(gapBadgeStatus("error"), null);
  assert.equal(gapBadgeStatus("partial_response"), null);
  // Real publisher statuses keep their badge.
  assert.equal(gapBadgeStatus("filter_not_supported_by_index"),
    "filter_not_supported_by_index");
  assert.equal(gapBadgeStatus("no_changes_in_period"), "no_changes_in_period");
  assert.equal(gapBadgeStatus("no_result"), "no_result");
  assert.equal(gapBadgeStatus("unknown_work"), "unknown_work");
});

// ---------------------------------------------------------------------------
// Tool authority (round 4, O6)
// ---------------------------------------------------------------------------

test("a single-tool surface accepts only its own operation's limitations", () => {
  const items = limitationsFromEffect([
    { status: LIMITATION_STATUS, tool: "search", publisher: "lu-legilux",
      unsupported_filters: ["domain"] },
    { status: LIMITATION_STATUS, tool: "in_force_on", publisher: "lu-legilux",
      unsupported_filters: ["domain"] },
  ]);
  assert.equal(items.length, 2, "same shape, different tools, both survive dedup");
  assert.equal(limitationsForTool(items, "search").length, 1);
  assert.equal(limitationsForTool(items, "search")[0].tool, "search");
  assert.equal(limitationsForTool(items, "in_force_on").length, 1);
  assert.equal(limitationsForTool(items, "changes_in_period").length, 0);
});

// ---------------------------------------------------------------------------
// Validation surface retained from earlier rounds
// ---------------------------------------------------------------------------

test("malformed limitation objects are ignored and never render", () => {
  assert.equal(validateLimitation(null), null);
  assert.equal(validateLimitation("x"), null);
  assert.equal(validateLimitation({ status: "ok", tool: "search" }), null);
  assert.equal(validateLimitation({ status: LIMITATION_STATUS, tool: "evil" }), null);
  assert.equal(validateLimitation({
    status: LIMITATION_STATUS, tool: "search", unsupported_filters: [],
  }), null);
  assert.equal(validateLimitation({
    status: LIMITATION_STATUS, tool: "search", unsupported_filters: ["not_governed"],
  }), null);
  const hostilePublisher = validateLimitation({
    status: LIMITATION_STATUS, tool: "search", publisher: "../../etc",
    unsupported_filters: ["domain"],
  });
  assert.equal(hostilePublisher?.publisher, undefined, "hostile ids drop to anonymous");
});

test("more than eight refusal envelopes are capped, never summarized into prose", () => {
  const many = Array.from({ length: 12 }, (_, index) => refused(`pub-${index}`, ["domain"]));
  assert.equal(partitionGovernedResponse("search", many).limitations.length, LIMITATION_CAP);
});

test("two separately allocated identical limitations render once", () => {
  const duplicated = partitionGovernedResponse("search", [
    refused("lu-legilux", ["domain", "hierarchy"]),
    refused("lu-legilux", ["hierarchy", "domain"]),
  ]);
  assert.equal(duplicated.limitations.length, 1, "logical identity, not reference identity");
});

test("the fixed explanation carries no interpolation and no query placeholder", () => {
  assert.ok(!LIMITATION_EXPLANATION.includes("{"));
  assert.ok(!LIMITATION_EXPLANATION.includes("$"));
  assert.ok(LIMITATION_EXPLANATION.includes("not evidence that"));
});

test("an ungoverned tool derives nothing even from a refusing envelope", () => {
  assert.equal(classifyEnvelope("provenance", refused("lu-legilux", ["domain"])).kind,
    "invalid");
  assert.equal(partitionGovernedResponse("provenance",
    [refused("lu-legilux", ["domain"])]).limitations.length, 0);
});
