import assert from "node:assert/strict";
import test from "node:test";
import {
  classifyEnvelope, clearedSearchResults, gapBadgeStatus, INCOMPLETE_RESPONSE_SENTENCE,
  AMBIGUOUS_ONLY_SENTENCE, LIMITATION_CAP, NO_CORPUS_SENTENCE, NO_CORPUS_STATUS,
  conflictedPublishersSentence, PARTIAL_RESPONSE_SENTENCE, scopedLimitations,
  LIMITATION_EXPLANATION, LIMITATION_STATUS, limitationsForTool, limitationsFromEffect,
  MIXED_ZERO_SENTENCES, partitionGovernedResponse, projectGovernedEmptiness,
  projectSearchResponse, searchAbsenceState, searchEmptyPresentation, searchResultsFromError,
  validateLimitation,
  GOVERNED_FILTERS, MANIFEST_FILTERS,
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
  retrieval_mode: "keyword",
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
    // O3: a successful search must declare the producer's actual retrieval mode.
    ["search", { envelope: { status: "ok" }, hits: [] }, "invalid"],
    ["search", { envelope: { status: "ok" }, hits: [], retrieval_mode: "guess" }, "invalid"],
    ["search", { envelope: { status: "ok" }, hits: [], retrieval_mode: "keyword" }, "ran"],
    // O4: a null row is not a row. Round 6 admitted it and then threw on lex_id.
    ["search", { envelope: { status: "ok" }, hits: [null], retrieval_mode: "keyword" },
      "invalid"],
    ["changes_in_period", { envelope: { status: "ok" }, changes: [null], works_changed: 1 },
      "invalid"],
    ["in_force_on", { envelope: { status: "ok" }, works: [null], total_works_in_force: 1 },
      "invalid"],
    ["in_force_on", { envelope: { status: "ambiguous_version" }, works: [],
      ambiguous_works: [null], total_works_in_force: 1 }, "invalid"],
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

test("a count outside the producer's own integer range is malformed, not merely large", () => {
  // O19. The authoritative chain is C# int end to end, checked rather than assumed:
  // IndexReader.ChangeTotals is declared `(int Works, int Versions)` and reads its columns with
  // GetInt32; Rows.InForcePage.TotalGroups is `int` and InForceOn computes it into a local
  // `int total`; McpCore publishes exactly those values as works_changed, new_versions and
  // total_works_in_force; UiMapper reads them back with GetValue<int>(). No long anywhere.
  //
  // Number.isFinite plus Number.isInteger is not that range. 1e20 and 2^53 are exact doubles,
  // both answer true to Number.isInteger, and both passed the count/row coherence checks
  // untouched: a legal count nothing could have minted then reached the screen as fact.
  const unmintable = [1e20, 2147483648, Number.MAX_SAFE_INTEGER, 2 ** 53, 1e308];
  for (const count of unmintable) {
    assert.equal(classifyEnvelope("in_force_on", {
      envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
      total_works_in_force: count,
    }).kind, "invalid", `in_force_on accepted total_works_in_force ${count}`);
    assert.equal(classifyEnvelope("changes_in_period", {
      envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
      works_changed: count, new_versions: 1,
    }).kind, "invalid", `changes_in_period accepted works_changed ${count}`);
    // The secondary aggregate rides the same producer type, so it carries the same bound.
    assert.equal(classifyEnvelope("changes_in_period", {
      envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
      works_changed: 1, new_versions: count,
    }).kind, "invalid", `changes_in_period accepted new_versions ${count}`);
  }
  // Int32.MaxValue itself is a count the producer can mint, so this is a bound, not a narrowing.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
    total_works_in_force: 2147483647,
  }).kind, "ran", "the producer's own maximum was refused");
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
    works_changed: 2147483647, new_versions: 2147483647,
  }).kind, "ran", "the producer's own maximum was refused");
  // Fail closed, exactly as a malformed shape does: no rows, no count, no absence claim.
  const projected = projectGovernedEmptiness("in_force_on", [{
    envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
    total_works_in_force: 1e20,
  }], 0);
  assert.deepEqual(projected.partition.ran, [], "an unmintable count authorized rows");
  assert.equal(projected.partition.invalidCount, 1);
  assert.equal(projected.empty, "incomplete_response",
    "an unmintable count was allowed to speak for the corpus");
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
  // Not absence, and not a result either. Round 6 returned null here, which let the caller
  // render a positive total beside an empty list (PR293 review, O2).
  assert.equal(decision.empty, "ambiguous_only");
  assert.ok(!AMBIGUOUS_ONLY_SENTENCE.toLowerCase().includes("nothing"),
    "the copy never claims absence");
  assert.ok(AMBIGUOUS_ONLY_SENTENCE.includes("Choose an exact publisher version"),
    "the copy asks for the clarification the publisher requires");
});

test("partiality is disclosed, not merely computed", () => {
  // Round 6 computed partial_results and decision.partial and rendered neither, so an
  // incomplete answer presented itself as complete (PR293 review, O1).
  const searchPartial = projectSearch([
    searchOk("lu-legilux", 2),
    { envelope: { status: "made_up" }, hits: [] },
  ]);
  assert.equal(searchPartial.absence, "partial_results");
  assert.ok(PARTIAL_RESPONSE_SENTENCE.includes("not everything"),
    "the disclosure states the answer is incomplete");
  assert.ok(!PARTIAL_RESPONSE_SENTENCE.includes("{"), "fixed copy, no interpolation");

  const governedPartial = projectGovernedEmptiness("in_force_on", [
    inForceOk("lu-legilux", 2),
    { envelope: { status: "made_up" }, works: [] },
  ], 2);
  assert.equal(governedPartial.empty, null);
  assert.equal(governedPartial.partial, true);
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


// ---------------------------------------------------------------------------
// The no-corpus refusal is GLOBAL and TERMINAL, so it speaks only for a whole response
// ---------------------------------------------------------------------------
//
// `McpCore.CallToolCore` returns it as one top-level object, with no envelope, before any
// per-publisher work runs. Two consequences, both load-bearing: it can never legitimately
// arrive beside a unit that answered, and an object carrying an envelope beside the terminal
// status is not the shape the producer emits.

/** The producer's actual terminal object, diagnostics included. */
const noCorpusUnit = (tool = "search") => ({
  status: NO_CORPUS_STATUS,
  detail: "This server started with zero verified indexes, so it holds no law.",
  hosted_endpoint: "https://law.soufien.lu/mcp",
  index_dir_searched: "indexes",
  tool_called: tool,
});

test("only the producer's single bare terminal object renders the no-corpus sentence", () => {
  // The repo requires the terminal case to keep working: a repo-built container mounts zero
  // indexes and must answer no_corpus_mounted rather than an empty list. It requires exactly
  // that ONE case. McpCore returns the object globally, BEFORE publisher iteration, so it can
  // only ever return one; a response carrying two is not a conservative case to preserve, it is
  // a shape the producer cannot emit.
  const one = projectSearch([noCorpusUnit()]);
  assert.equal(one.absence, "no_corpus", "the one shape the repo requires stopped working");
  assert.equal(searchEmptyPresentation("no_corpus").sentence, NO_CORPUS_SENTENCE);
  assert.equal(partitionGovernedResponse("search", [noCorpusUnit()]).noCorpus, true);
  // A bare object rather than a one-element array is the same single unit.
  assert.equal(projectSearch(noCorpusUnit()).absence, "no_corpus");
  for (const tool of ["changes_in_period", "in_force_on"]) {
    assert.equal(projectGovernedEmptiness(tool, [noCorpusUnit(tool)], 0).empty,
      "no_corpus", `${tool} lost the terminal state`);
  }
});

test("a repeated no-corpus pair is a shape the producer cannot emit, so it claims nothing", () => {
  // The correction to the first reading of this objection. The terminal object is returned
  // before publisher iteration, so two of them is not a stronger no-corpus statement; it is a
  // response nothing stands behind, and it must not authorize a sentence about the corpus.
  const pair = partitionGovernedResponse("search", [noCorpusUnit(), noCorpusUnit()]);
  assert.equal(pair.noCorpus, false, "a shape the producer cannot emit authorized the sentence");
  assert.equal(pair.invalidCount, 2, "the incoherent units were ignored rather than counted");
  assert.equal(searchAbsenceState(pair, 0), "incomplete_response");
  assert.notEqual(projectSearch([noCorpusUnit(), noCorpusUnit(), noCorpusUnit()]).absence,
    "no_corpus");
  assert.equal(projectSearch([noCorpusUnit(), noCorpusUnit()]).absence, "incomplete_response");
  for (const tool of ["changes_in_period", "in_force_on"]) {
    assert.equal(
      projectGovernedEmptiness(tool, [noCorpusUnit(tool), noCorpusUnit(tool)], 0).empty,
      "incomplete_response", `${tool} accepted a repeated terminal object`);
  }
});

test("a no-corpus unit beside anything that answered never claims the corpus is empty", () => {
  // Counterexample 1 of the objection: the sentence was rendered beside a sibling asserting a
  // mounted publisher and index boundary, so it was FALSE on screen. Each sibling class is
  // exercised separately, because the old sibling check omitted mode_unavailable entirely.
  const siblings: [string, unknown][] = [
    ["ran", searchOk("lu-legilux", 0)],
    ["refused", refused("lu-legilux", ["domain"])],
    ["mode_unavailable",
      { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } }],
    ["invalid", { envelope: { status: "made_up" }, hits: [] }],
  ];
  for (const [label, sibling] of siblings) {
    for (const order of [[noCorpusUnit(), sibling], [sibling, noCorpusUnit()]]) {
      const partition = partitionGovernedResponse("search", order);
      assert.equal(partition.noCorpus, false,
        `no_corpus survived beside a ${label} sibling`);
      assert.ok(partition.invalidCount > 0,
        `the incoherent no_corpus unit was ignored beside a ${label} sibling`);
      const projected = projectSearch(order);
      assert.notEqual(projected.absence, "no_corpus",
        `the false terminal sentence rendered beside a ${label} sibling`);
      assert.equal(projected.absence, "incomplete_response",
        `an incoherent response claimed something beside a ${label} sibling`);
    }
  }
});

test("a no-corpus unit beside rendered rows is disclosed, never dropped", () => {
  // Counterexample 2: whole results rendered and the contradictory global state vanished,
  // because the no-corpus class did not increase invalidCount. The rows still render, but the
  // answer must present itself as partial.
  const projected = projectSearch([searchOk("lu-legilux", 2), noCorpusUnit()]);
  assert.equal(projected.works.length, 2, "the usable publisher's rows still render");
  assert.equal(projected.absence, "partial_results",
    "an answer was shown beside a response saying the server holds no law");
  const governed = projectGovernedEmptiness(
    "in_force_on", [inForceOk("lu-legilux", 2), noCorpusUnit("in_force_on")], 2);
  assert.equal(governed.empty, null);
  assert.equal(governed.partial, true, "the contradictory global state was silently dropped");
});

test("a no-corpus object carrying an envelope is forged, not terminal", () => {
  // The producer returns the terminal object before any per-publisher work, so it carries no
  // envelope. Accepting the status before inspecting the envelope let one object assert a
  // mounted publisher AND authorize the sentence saying nothing is mounted.
  const forged = {
    ...noCorpusUnit(),
    envelope: { status: "ok", publisher: "lu-legilux" },
    retrieval_mode: "keyword",
    hits: [{ lex_id: "lu-legilux:w0" }],
  };
  assert.equal(classifyEnvelope("search", forged).kind, "invalid");
  const projected = projectSearch([forged]);
  assert.notEqual(projected.absence, "no_corpus");
  assert.equal(projected.absence, "incomplete_response");
  assert.deepEqual(projected.works, [], "a forged terminal object rendered rows");
  // A null envelope is not an absent one either.
  assert.equal(classifyEnvelope("search", { ...noCorpusUnit(), envelope: null }).kind,
    "invalid");
  // And the producer's own extra diagnostic fields must stay acceptable.
  assert.equal(classifyEnvelope("search", noCorpusUnit()).kind, "no_corpus",
    "the documented diagnostic fields were treated as a foreign shape");
});

test("a mixed no-corpus response never selects the all-refused coverage copy", () => {
  // all_refused says "No selected publisher ran this query", which is a coverage claim about
  // publishers. A response that also claims no index is mounted cannot support it.
  const mixed = partitionGovernedResponse("search",
    [noCorpusUnit(), refused("lu-legilux", ["domain"])]);
  assert.equal(mixed.allRefused, false);
  assert.equal(searchAbsenceState(mixed, 0), "incomplete_response");
  // A lone genuine refusal still reaches all_refused, so this is not a blanket suppression.
  const lone = partitionGovernedResponse("search", [refused("lu-legilux", ["domain"])]);
  assert.equal(lone.allRefused, true);
  assert.equal(searchAbsenceState(lone, 0), "all_refused");
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



// ---------------------------------------------------------------------------
// Same-publisher terminal-kind conflict, at the shared projector, for every governed tool
// ---------------------------------------------------------------------------
//
// One publisher cannot both have run an operation and refused it. A response carrying both
// contains two claims and nothing that says which is true, so keeping either side is the
// product choosing one and asserting it. This lived in the search normalizer only, which left
// changes_in_period and in_force_on emitting ran=1, limitation=1, invalidCount=0, partial=false.

/** A coherent refusal for one publisher, on any governed tool. */
const refusalFor = (publisher: string) => refused(publisher, ["domain"]);

/** The coherent success fixture for each governed tool, keyed by tool. */
const ranFor: Record<string, (publisher: string, rows: number) => unknown> = {
  search: (publisher, rows) => searchOk(publisher, rows),
  changes_in_period: (publisher, rows) => changesOk(publisher, rows),
  in_force_on: (publisher, rows) => inForceOk(publisher, rows),
};

test("ran beside refused for one publisher withholds both, on every governed tool", () => {
  for (const tool of Object.keys(ranFor)) {
    const ranEntry = ranFor[tool]!("lu-legilux", 2);
    const refusal = refusalFor("lu-legilux");
    // Order reversal: arrival order may not decide which side the reader is shown.
    for (const order of [[ranEntry, refusal], [refusal, ranEntry]]) {
      const partition = partitionGovernedResponse(tool, order);
      assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"],
        `${tool}: the conflict was not detected`);
      assert.deepEqual(partition.ran, [], `${tool}: the ran side was asserted as fact`);
      assert.equal(partition.limitations.length, 0,
        `${tool}: the refusal side was asserted as fact`);
      assert.equal(partition.allRefused, false,
        `${tool}: a response that said it ran selected the no-publisher-ran copy`);
      assert.equal(partition.invalidCount, 2,
        `${tool}: the incoherence was dropped rather than retained`);
    }
  }
});

test("an all-conflicted governed response is incomplete, never an absence claim", () => {
  for (const tool of ["changes_in_period", "in_force_on"]) {
    const decision = projectGovernedEmptiness(tool,
      [ranFor[tool]!("lu-legilux", 2), refusalFor("lu-legilux")], 0);
    assert.equal(decision.empty, "incomplete_response", `${tool} claimed something`);
    assert.equal(decision.partial, true, `${tool} hid the incoherence`);
  }
  const projected = projectSearch([searchOk("lu-legilux", 2), refusalFor("lu-legilux")]);
  assert.deepEqual(projected.works, [], "a contradicted publisher's rows rendered");
  assert.deepEqual(projected.limitations, []);
  assert.equal(projected.absence, "incomplete_response");
});

test("another publisher renders beside a conflict, but only as partial", () => {
  // The uncontradicted publisher keeps its rows. The answer may not present itself as whole.
  const projected = projectSearch([
    searchOk("eu-eurlex", 2),
    searchOk("lu-legilux", 3),
    refusalFor("lu-legilux"),
  ]);
  assert.equal(projected.works.length, 2, "the uncontradicted publisher lost its rows");
  assert.equal(projected.absence, "partial_results",
    "a response containing a self-contradicting publisher presented itself as whole");
  const governed = projectGovernedEmptiness("in_force_on", [
    inForceOk("eu-eurlex", 2), inForceOk("lu-legilux", 3), refusalFor("lu-legilux"),
  ], 2);
  assert.equal(governed.empty, null);
  assert.equal(governed.partial, true);
});

test("a search conflict between ran and an unavailable retrieval mode withholds both", () => {
  // The third pairing, and the one that only search can produce.
  const mode = { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } };
  for (const order of [[searchOk("lu-legilux", 2), mode], [mode, searchOk("lu-legilux", 2)]]) {
    const partition = partitionGovernedResponse("search", order);
    assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"]);
    assert.deepEqual(partition.ran, []);
    assert.equal(partition.modeUnavailableCount, 0,
      "a mode claim survived from a publisher that also said it ran");
    assert.deepEqual(partition.modeUnavailablePublishers, []);
    assert.equal(searchAbsenceState(partition, 0), "incomplete_response");
  }
  // The mode notice must not name a publisher whose claim was withheld.
  const projected = projectSearch([searchOk("lu-legilux", 2), mode]);
  assert.equal(projected.modeUnavailable, undefined);
});

test("a refusal beside an unavailable mode for one publisher withholds both", () => {
  const mode = { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } };
  const partition = partitionGovernedResponse("search", [refusalFor("lu-legilux"), mode]);
  assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"]);
  assert.equal(partition.limitations.length, 0);
  assert.equal(partition.modeUnavailableCount, 0);
  assert.equal(partition.allRefused, false);
  assert.equal(searchAbsenceState(partition, 0), "incomplete_response");
});

test("a conflict across distinct publishers is not a conflict, on every governed tool", () => {
  // The guard against over-correction. lu-legilux ran and eu-eurlex refused: nothing
  // contradicts itself, so both disclosures stand.
  for (const tool of Object.keys(ranFor)) {
    const partition = partitionGovernedResponse(tool,
      [ranFor[tool]!("lu-legilux", 2), refusalFor("eu-eurlex")]);
    assert.deepEqual(partition.conflictedPublishers, [], `${tool}: invented a conflict`);
    assert.equal(partition.ran.length, 1, `${tool}: dropped an uncontradicted publisher`);
    assert.equal(partition.limitations.length, 1, `${tool}: dropped a real limitation`);
    assert.equal(partition.invalidCount, 0, `${tool}: reported a coherent response as partial`);
  }
});

test("a lone genuine refusal still reaches all_refused, on every governed tool", () => {
  // The other guard: this is the case the copy about coverage exists for, and it must survive.
  for (const tool of Object.keys(ranFor)) {
    const partition = partitionGovernedResponse(tool, [refusalFor("lu-legilux")]);
    assert.deepEqual(partition.conflictedPublishers, []);
    assert.equal(partition.limitations.length, 1, `${tool}: silenced a lone refusal`);
    assert.equal(partition.allRefused, true, `${tool}: lost the coverage state`);
  }
  assert.equal(projectSearch([refusalFor("lu-legilux")]).absence, "all_refused");
  assert.equal(
    projectGovernedEmptiness("in_force_on", [refusalFor("lu-legilux")], 0).empty, "all_refused");
});

test("a second unit from one publisher is incoherent whatever it says", () => {
  // This test previously asserted the opposite, that two identical refusals collapse into one
  // disclosure, on the reasoning that the same kind twice is agreement rather than contradiction.
  // The producer says otherwise. IndexRegistry keys its readers by collection with an ordinal
  // comparer and the tools iterate its values, so at most one unit per publisher can ever be
  // emitted. A second unit is a shape the producer cannot produce, and byte identity does not
  // make it legitimate: two ran units for one publisher doubled the works changed, the new
  // versions, the population and the rows, and nothing distinguishes that case from this one
  // except what the duplicate happens to say.
  const partition = partitionGovernedResponse("search",
    [refusalFor("lu-legilux"), refusalFor("lu-legilux")]);
  assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"]);
  assert.equal(partition.limitations.length, 0, "a duplicated publisher still spoke");
  assert.equal(partition.allRefused, false, "an incoherent response became an absence claim");
});

test("an unattributable claim neither conflicts with a named one nor speaks itself", () => {
  // Two properties, and the second one changed. A padded spelling must not be grouped with the
  // real publisher, or one publisher's rows would be withheld by another's malformed unit; that
  // half is unchanged and is the anti-aliasing guarantee.
  //
  // What changed is that the unattributable unit no longer renders its own limitation. The comment
  // here used to say it was handled as an unattributed entry upstream, which is true of the search
  // surface and false of the other governed tools: they reach this projector directly, so a
  // missing, padded, upper-case or overlong publisher could render a limitation with no index
  // identity beside it. The envelope always carries the reader's collection, so a claim without a
  // bounded identity is malformed rather than merely anonymous.
  const nameless = { envelope: { status: "filter_not_supported_by_index", publisher: " lu-legilux " },
    unsupported_filters: ["domain"] };
  const partition = partitionGovernedResponse("search", [searchOk("lu-legilux", 2), nameless]);
  assert.deepEqual(partition.conflictedPublishers, [],
    "a padded spelling was grouped with the real publisher");
  assert.equal(partition.ran.length, 1, "an uncontradicted publisher lost its rows");
  assert.equal(partition.limitations.length, 0, "an unattributable claim still spoke");
  assert.equal(partition.invalidCount, 1, "the withheld unit left no trace on completeness");
});

// ---------------------------------------------------------------------------
// Publisher identity: one grammar with the strip and the population footer
// ---------------------------------------------------------------------------

test("a limitation publisher is validated by the shared non-normalizing grammar", () => {
  // This module used to validate publisher through the same case-insensitive general
  // identifier it uses for jurisdiction. Producer registry identities are ordinal lower-case,
  // so a case alias is a publisher the producer cannot mint. See publisherIdentity.ts.
  const of = (publisher: unknown) => validateLimitation({
    status: LIMITATION_STATUS, tool: "search", publisher,
    unsupported_filters: ["domain"],
  });
  assert.equal(of("lu-legilux")?.publisher, "lu-legilux");
  for (const bad of [" lu-legilux ", "lu-legilux ", " lu-legilux", "LU-LEGILUX", "LU-Legilux",
                     "lu-legiluX", "?", "lu legilux", "x".repeat(65), 7, null, {}]) {
    const limitation = of(bad);
    assert.notEqual(limitation, null, "a bad publisher must not void the limitation itself");
    assert.equal(limitation!.publisher, undefined,
      `accepted publisher ${JSON.stringify(bad)}`);
  }
  assert.notEqual(of(" lu-legilux ")?.publisher, "lu-legilux",
    "a padded publisher was trimmed into the real identity");
  assert.notEqual(of("LU-Legilux")?.publisher, "lu-legilux",
    "a case alias was folded into the real identity");
});

test("a padded and an unpadded publisher are two limitations, never merged into one", () => {
  // Dedup keys on the validated publisher. Trimming would collapse these two into one row and
  // attribute a refusal to a publisher that never made it; refusing the padded one keeps them
  // distinct AND keeps the unattributable refusal visible with no publisher name on it.
  const items = limitationsFromEffect([
    { status: LIMITATION_STATUS, tool: "search", publisher: "lu-legilux",
      unsupported_filters: ["domain"] },
    { status: LIMITATION_STATUS, tool: "search", publisher: " lu-legilux ",
      unsupported_filters: ["domain"] },
  ]);
  assert.equal(items.length, 2, "the padded spelling was merged into the real publisher");
  assert.deepEqual(items.map((i) => i.publisher), ["lu-legilux", undefined]);
});

test("jurisdiction keeps its own vocabulary and is not routed through publisher", () => {
  // Different vocabularies, different grammars, and the producer says so: McpCore.SelectReaders
  // compares publisher ordinally and jurisdiction OrdinalIgnoreCase in one expression, and
  // LegalOperationCatalog documents the jurisdiction argument as "e.g. LU or EU". Upper case is
  // a jurisdiction the producer really emits, so refusing it here would drop a disclosed field.
  const of = (jurisdiction: unknown) => validateLimitation({
    status: LIMITATION_STATUS, tool: "search", publisher: "lu-legilux", jurisdiction,
    unsupported_filters: ["domain"],
  });
  assert.equal(of("LU")?.jurisdiction, "LU", "an uppercase jurisdiction code was refused");
  assert.equal(of("EU")?.jurisdiction, "EU");
  assert.equal(of("lu")?.jurisdiction, "lu");
  // Still bounded and still closed, just on its own axis.
  for (const bad of [" LU ", "L U", "", "x".repeat(65), 7, null]) {
    assert.equal(of(bad)?.jurisdiction, undefined,
      `accepted jurisdiction ${JSON.stringify(bad)}`);
  }
});

test("a mode-unavailable publisher uses the same grammar as everything else", () => {
  const alias = classifyEnvelope("search", {
    envelope: { status: "retrieval_mode_unavailable", publisher: "LU-Legilux" },
  });
  assert.equal(alias.kind, "mode_unavailable");
  assert.equal(alias.kind === "mode_unavailable" && alias.publisher, undefined,
    "a case alias was named as an unavailable publisher");
  const padded = classifyEnvelope("search", {
    envelope: { status: "retrieval_mode_unavailable", publisher: " lu-legilux " },
  });
  assert.equal(padded.kind === "mode_unavailable" && padded.publisher, undefined);
  const real = classifyEnvelope("search", {
    envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" },
  });
  assert.equal(real.kind === "mode_unavailable" && real.publisher, "lu-legilux",
    "the fixture never reached the validator with an acceptable identity");
});

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

  // Round-5 O5: the renderer must call the tested seam, not a private copy of it. This is
  // the exact function views.tsx invokes, so a mutation here changes what ships.
  assert.equal(scopedLimitations(items, "search").length, 1);
  assert.equal(scopedLimitations(items, "search")[0].tool, "search");
  assert.equal(scopedLimitations(items, "changes_in_period").length, 0);
  // An undefined tool is the sanctioned multi-operation surface: nothing is filtered out,
  // and the component labels each row's authority instead.
  assert.equal(scopedLimitations(items, undefined).length, 2);
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

test("two units for one publisher are withheld even when logically identical", () => {
  // Also inverted. It asserted that two separately allocated but logically identical limitations
  // render once, which was a statement about deduplication by value rather than by reference.
  // That property is real, but this fixture is not the place it applies: the producer cannot send
  // one publisher twice, so the response is incoherent before deduplication becomes a question.
  const duplicated = partitionGovernedResponse("search", [
    refused("lu-legilux", ["domain", "hierarchy"]),
    refused("lu-legilux", ["hierarchy", "domain"]),
  ]);
  assert.deepEqual(duplicated.conflictedPublishers, ["lu-legilux"]);
  assert.equal(duplicated.limitations.length, 0);
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

// ---------------------------------------------------------------------------
// PR293 exact review: row schemas, status coherence, ambiguity rendering
// ---------------------------------------------------------------------------

test("a row a renderer cannot read is malformed, not empty", () => {
  // Round 7 accepted any non-null object, so hits:[{}] was ran: the empty row yielded no
  // work id, the surface found zero rows and claimed nothing in the corpus matched.
  assert.equal(classifyEnvelope("search", {
    envelope: { status: "ok" }, hits: [{}], retrieval_mode: "keyword",
  }).kind, "invalid");
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "ok" }, changes: [{}], works_changed: 1,
  }).kind, "invalid", "an empty change row threw inside work.includes");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok" }, works: [{}], total_works_in_force: 1,
  }).kind, "invalid");
  // A row carrying what the renderer actually consumes is fine.
  assert.equal(classifyEnvelope("search", {
    envelope: { status: "ok" }, hits: [{ lex_id: "lu:w1:2024-01-01" }],
    retrieval_mode: "keyword",
  }).kind, "ran");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok" }, works: [{ lex_id: "lu:w1:2024-01-01" }],
    total_works_in_force: 1,
  }).kind, "ran", "in-force rows may be identified by lex_id instead of work");
});

test("status and counts must cohere", () => {
  // no_result means the publisher found nothing, so a positive total contradicts it.
  // Round 7 admitted this and rendered a false claim that no state covers the date
  // beside a response reporting five.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "no_result" }, works: [], total_works_in_force: 5,
  }).kind, "invalid");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "no_result" }, works: [], total_works_in_force: 0,
  }).kind, "ran");
  // ambiguous_version asserts at least one ambiguity unit exists.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ambiguous_version" }, works: [], ambiguous_works: [],
    total_works_in_force: 0,
  }).kind, "invalid");
  // no_changes_in_period must be empty on every count it carries.
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "no_changes_in_period" }, changes: [], works_changed: 0,
    new_versions: 3,
  }).kind, "invalid");
});

test("ambiguity units travel to the caller for rendering", () => {
  // Round 7 chose visible rows first and dropped these objects while keeping their
  // contribution to the total, producing pagination with an unexplained extra unit.
  const mixed = {
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    works: [{ work: "w1", title: "Determinate", valid_from: "2024-01-01" }],
    ambiguous_works: [{ work: "w2", title: "Ambiguous", valid_from: "2024-01-01" }],
    total_works_in_force: 2,
  };
  const decision = projectGovernedEmptiness("in_force_on", [mixed], 1);
  assert.equal(decision.empty, null, "a normal row exists, so this is a result");
  assert.equal(decision.ambiguous.length, 1, "the ambiguity unit reaches the caller");
  assert.equal((decision.ambiguous[0] as { work: string }).work, "w2");

  // And the invalid-sibling disclosure survives the ambiguity-only branch.
  const ambiguousPlusInvalid = projectGovernedEmptiness("in_force_on", [
    { envelope: { status: "ambiguous_version", publisher: "lu-legilux" }, works: [],
      ambiguous_works: [{ work: "w2", valid_from: "2024-01-01" }],
      total_works_in_force: 1 },
    { envelope: { status: "made_up" }, works: [] },
  ], 0);
  assert.equal(ambiguousPlusInvalid.empty, "ambiguous_only");
  assert.equal(ambiguousPlusInvalid.partial, true,
    "an unusable sibling is still disclosed beside the ambiguity message");
});

// Legacy work-catalog guard (Codex contract amendment, 2026-08-28). publisher_metadata_identifier
// is answerable only by an extended work catalog; an older index evaluates it as an ordinary
// predicate and returns an authoritative-looking zero, so the producer refuses instead. The client
// must recognize that filter or it turns an honest refusal into a malformed response, which is
// never-implied rule 10. These were watched failing before the vocabulary was widened.

test("a capability refusal naming publisher_metadata_identifier is honest, not malformed", () => {
  const refusal = {
    envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["publisher_metadata_identifier"],
  };
  assert.equal(classifyEnvelope("search", refusal).kind, "refused",
    "an honest capability refusal must never be presented as a malfunction");
});

test("the guarded filter is not smuggled into the signed capability manifest", () => {
  // The index checks four filters and its manifest is signed; widening that set would invalidate
  // every stamp. The client's accepted vocabulary is deliberately wider than the manifest, and
  // this pins the divergence so nobody collapses the two sets back together.
  assert.deepEqual([...MANIFEST_FILTERS].sort(),
    ["act_form", "binding_status", "domain", "hierarchy"]);
  assert.equal(MANIFEST_FILTERS.has("publisher_metadata_identifier"), false);
  assert.equal(GOVERNED_FILTERS.has("publisher_metadata_identifier"), true);
  for (const f of MANIFEST_FILTERS) assert.equal(GOVERNED_FILTERS.has(f), true);
});

test("a genuinely unknown filter is still dropped", () => {
  assert.equal(classifyEnvelope("search", {
    envelope: { status: LIMITATION_STATUS, publisher: "lu-legilux" },
    unsupported_filters: ["not_a_governed_filter"],
  }).kind, "invalid");
});

// A conflicted publisher is not merely missing from the answer. Every claim it made was withheld,
// so a reader deciding whether this answer covers what they care about needs to know which
// publisher it was. "These results are incomplete" alone does not tell them that.
//
// The copy used to say the publisher answered in ways that contradict each other. That was true
// while a conflict meant its units disagreed about kind, and it is false now: a publisher is
// conflicted on the unit COUNT alone, so a second unit that is byte-identical to the first is
// withheld while contradicting the first in no way whatever. These tests pin the wording that
// holds for BOTH causes and forbid the one that holds for only one of them.

/** Every phrasing that asserts the units disagreed, which is the reading being retired. */
const DISAGREEMENT_CLAIMS =
  ["contradict", "conflict", "disagree", "inconsisten", "each other", "differ"];

test("no conflicted publisher says nothing at all", () => {
  assert.equal(conflictedPublishersSentence([]), undefined);
});

test("a conflicted publisher is named, not counted", () => {
  const sentence = conflictedPublishersSentence(["lu-legilux"]);
  assert.ok(sentence !== undefined);
  assert.ok(sentence.includes("lu-legilux"), "the publisher was not named");
  assert.ok(!sentence.includes("1 publisher"), "named, not counted");
  assert.ok(!/\bone publisher\b/.test(sentence), "named, not counted");
  for (const claim of DISAGREEMENT_CLAIMS) {
    assert.ok(!sentence.toLowerCase().includes(claim),
      `the copy asserts disagreement with "${claim}"`);
  }
});

test("several conflicted publishers are all named", () => {
  const sentence = conflictedPublishersSentence(["eu-eurlex", "lu-legilux"]) ?? "";
  assert.ok(sentence.includes("eu-eurlex") && sentence.includes("lu-legilux"));
  assert.ok(!sentence.includes("2 publishers"), "named, not counted");
  // The list can carry several, so singular copy about "that publisher" misreads on screen.
  assert.ok(!sentence.includes("that publisher"), "singular copy about a list of publishers");
  for (const claim of DISAGREEMENT_CLAIMS) {
    assert.ok(!sentence.toLowerCase().includes(claim),
      `the copy asserts disagreement with "${claim}"`);
  }
});

test("the copy states the one fact both causes share and asserts no disagreement", () => {
  const sentence = conflictedPublishersSentence(["lu-legilux"]) ?? "";
  // The shared fact: more than one answer arrived. True whether the extra unit disagreed with
  // the first or repeated it exactly, because the producer can send only one either way.
  assert.ok(sentence.includes("more than one"),
    "the sentence no longer states what is true in both cases");
  for (const claim of DISAGREEMENT_CLAIMS) {
    assert.ok(!sentence.toLowerCase().includes(claim),
      `the copy asserts disagreement with "${claim}"`);
  }
  // "scope figure" belongs to a different conflict entirely and must not be borrowed here.
  assert.ok(!sentence.includes("scope figure"), "a unit-count conflict relabelled as a scope one");
  assert.ok(!sentence.includes("{") && !sentence.includes("$"),
    "fixed copy beyond the publisher names");
});

test("the copy is true when the second unit is byte-identical to the first", () => {
  // The case that made the old wording false. Two refusals from one publisher with identical
  // bytes are still withheld, because the reader registry is keyed by collection and the producer
  // can emit at most one unit per publisher. They contradict each other in no way whatever, so a
  // sentence saying they did was a specific falsehood on screen.
  const first = refused("lu-legilux", ["domain"]);
  const second = refused("lu-legilux", ["domain"]);
  assert.equal(JSON.stringify(first), JSON.stringify(second),
    "the fixture is not actually a byte-identical duplicate");

  const partition = partitionGovernedResponse("search", [first, second]);
  assert.deepEqual(partition.conflictedPublishers, ["lu-legilux"],
    "an identical duplicate stopped being withheld, so this copy has no subject left");
  const sentence = conflictedPublishersSentence(partition.conflictedPublishers) ?? "";
  assert.ok(sentence.includes("lu-legilux"), "the publisher was not named");
  assert.ok(sentence.includes("more than one"),
    "the sentence must state what is actually true of two identical answers");
  for (const claim of DISAGREEMENT_CLAIMS) {
    assert.ok(!sentence.toLowerCase().includes(claim),
      `two identical answers were described as "${claim}"`);
  }
});
