import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import {
  absenceAuthorityIncomplete, RETRIEVAL_MODE_UNAVAILABLE_SENTENCE,
  classifyEnvelope, clearedSearchResults, gapBadgeStatus, INCOMPLETE_RESPONSE_SENTENCE,
  AMBIGUOUS_ONLY_SENTENCE, LIMITATION_CAP, NO_CORPUS_SENTENCE, NO_CORPUS_STATUS,
  conflictedPublishersSentence, PARTIAL_RESPONSE_SENTENCE, scopedLimitations,
  LIMITATION_EXPLANATION, LIMITATION_STATUS, limitationsForTool, limitationsFromEffect,
  MIXED_ZERO_SENTENCES, parseGovernedResponse, partitionGovernedResponse, partitionOf,
  projectGovernedEmptiness, withholdingSentence,
  projectSearchResponse, searchAbsenceState, searchEmptyPresentation, searchResultsFromError,
  validateLimitation,
  GOVERNED_FILTERS, MANIFEST_FILTERS,
} from "./limitations.ts";
// The withholding disclosure is rendered from the adapter's output, so the cause-preserving
// tests below drive the same chain Search.tsx does: one parse, one normalization, one
// sentence (O3).
import { normalizeSearchResponse } from "./searchPopulation.ts";

/**
 * THE POPULATION IS NOT OPTIONAL DRESSING ON THESE FIXTURES, and it did not used to be here.
 *
 * The producer publishes a population on every path these fixtures stand for, and the client now
 * treats an unreadable required scope as invalidating the whole claim rather than only the scope
 * field. A fixture without one is therefore a response the producer cannot emit, and every
 * builder below now carries the one its own status coheres with. See the corrected rule in
 * limitations.ts: a marker that records a problem and lets the unit go on authorizing rows is
 * the defect class this table exists to remove.
 */
const searchPopulationFor = (status: string, works = 1250) => ({
  basis: status === LIMITATION_STATUS
    ? "mounted_scope_before_unsupported_filters"
    : "selected_metadata_scope",
  works_in_scope: works,
  scope_filters_applied: status !== LIMITATION_STATUS,
  query_ran: status === "ok",
  known_exclusions: [],
});

/** `McpCore` changes_in_period: `basis`, `works_in_scope`, `expected_works`, exclusions. */
const changesPopulation = (works = 1250) => ({
  basis: "distinct non-withdrawn works in the selected publisher and legal metadata scope",
  works_in_scope: works,
  known_exclusions: [],
});

/** `McpCore` in_force_on: `works_covered` from `Coverage(1).Groups`, never filter-narrowed. */
const inForcePopulation = (works = 1250) => ({
  basis: "versioned works only",
  works_covered: works,
  known_exclusions: [],
});

/**
 * A capability refusal. The population is SEARCH ONLY: `McpCore` attaches one to the search
 * refusal (`refusal["population"] = SearchPopulation(reader, filter, false, false)`) and returns
 * `UnsupportedFilterResult` unchanged for the other two, so a changes or in-force refusal
 * carrying a population is a forgery rather than a harmless extra.
 */
const refused = (publisher: string, filters: string[], tool = "search") => ({
  envelope: {
    status: LIMITATION_STATUS, publisher,
    jurisdiction: publisher === "eu-eurlex" ? "eu" : "lu",
  },
  unsupported_filters: filters,
  ...(tool === "search" ? { population: searchPopulationFor(LIMITATION_STATUS) } : {}),
});

/** A coherent changes_in_period success: rows and counts agree. */
const changesOk = (publisher: string, rows: number) => ({
  envelope: { status: "ok", publisher },
  changes: Array.from({ length: rows }, (_, index) => ({ work: `w${index}` })),
  works_changed: rows,
  new_versions: rows,
  population: changesPopulation(),
});

/** A coherent search success. */
const searchOk = (publisher: string, hits: number, extra: Record<string, unknown> = {}) => ({
  envelope: { status: "ok", publisher },
  retrieval_mode: "keyword",
  hits: Array.from({ length: hits }, (_, index) => ({
    lex_id: `${publisher}:w${index}:2024-01-01`, title: `Work ${index}`,
    valid_from: "2024-01-01",
  })),
  population: searchPopulationFor("ok"),
  ...extra,
});

/**
 * A coherent retrieval-mode refusal. It carries a population because search publishes one on
 * this path too (`SearchPopulation(reader, filter, true, false)` beside
 * `McpStatus.RetrievalModeUnavailable`), and an unreadable required scope now invalidates the
 * whole claim rather than only the scope field.
 */
const modeUnavailable = (publisher: string) => ({
  envelope: { status: "retrieval_mode_unavailable", publisher },
  population: searchPopulationFor("retrieval_mode_unavailable"),
});

/** A coherent in_force_on success. */
const inForceOk = (publisher: string, rows: number) => ({
  envelope: { status: "ok", publisher },
  works: Array.from({ length: rows }, (_, index) => ({
    work: `w${index}`, title: `Work ${index}`, valid_from: "2024-01-01",
  })),
  total_works_in_force: rows,
  population: inForcePopulation(),
});

/**
 * The production search projection, with the component's own mapping shape. It takes raw bytes
 * and parses them HERE, once, exactly as Search.tsx does: the projector itself no longer accepts
 * a raw response, so a test cannot exercise a path production cannot reach.
 */
const projectSearch = (raw: unknown) =>
  projectSearchResponse<{ work: string }, { work: string }>(
    parseGovernedResponse("search", raw), (ran) => {
    const hits = ran.flatMap((unit) => unit.rows);
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
    ["search", { envelope: { status: "ok" }, hits: [], retrieval_mode: "keyword",
      population: searchPopulationFor("ok") }, "ran"],
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
    ["search", modeUnavailable("p"), "mode_unavailable"],
    // The corrected rule: search publishes a population on ALL THREE of its paths, so a claim
    // whose required scope is unreadable is invalid rather than a claim with a missing number.
    ["search", { envelope: { status: "retrieval_mode_unavailable", publisher: "p" } },
      "invalid"],
    ["search", { envelope: { status: "ok" }, hits: [], retrieval_mode: "keyword" }, "invalid"],
    ["changes_in_period", changesOk("lu-legilux", 1), "ran"],
    ["changes_in_period",
      { envelope: { status: "no_changes_in_period" }, changes: [], works_changed: 0,
        population: changesPopulation() }, "ran"],
    ["changes_in_period", { envelope: { status: "ok" }, changes: [] }, "invalid"],
    ["in_force_on", inForceOk("lu-legilux", 1), "ran"],
    ["in_force_on",
      { envelope: { status: "no_result" }, works: [], total_works_in_force: 0,
        population: inForcePopulation() }, "ran"],
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
  //
  // EVERY FIXTURE HERE CARRIES ITS POPULATION, and that is not decoration. A population is now
  // required on every ran path, so a fixture without one is invalid whatever its counts say, and
  // this test went green against a copy of requiredCount with the ceiling removed. It had
  // stopped testing the bound it is named for. Found by mutation, not by reading.
  const unmintable = [1e20, 2147483648, Number.MAX_SAFE_INTEGER, 2 ** 53, 1e308];
  for (const count of unmintable) {
    assert.equal(classifyEnvelope("in_force_on", {
      envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
      total_works_in_force: count, population: inForcePopulation(),
    }).kind, "invalid", `in_force_on accepted total_works_in_force ${count}`);
    assert.equal(classifyEnvelope("changes_in_period", {
      envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
      works_changed: count, new_versions: 1, population: changesPopulation(),
    }).kind, "invalid", `changes_in_period accepted works_changed ${count}`);
    // The secondary aggregate rides the same producer type, so it carries the same bound.
    assert.equal(classifyEnvelope("changes_in_period", {
      envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
      works_changed: 1, new_versions: count, population: changesPopulation(),
    }).kind, "invalid", `changes_in_period accepted new_versions ${count}`);
    // The population's own denominator rides it too, on both measures.
    assert.equal(classifyEnvelope("in_force_on", {
      envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
      total_works_in_force: 1, population: { ...inForcePopulation(), works_covered: count },
    }).kind, "invalid", `in_force_on accepted works_covered ${count}`);
  }
  // Int32.MaxValue itself is a count the producer can mint, so this is a bound, not a narrowing.
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
    total_works_in_force: 2147483647, population: inForcePopulation(),
  }).kind, "ran", "the producer's own maximum was refused");
  assert.equal(classifyEnvelope("changes_in_period", {
    envelope: { status: "ok", publisher: "lu-legilux" }, changes: [{ work: "w" }],
    works_changed: 2147483647, new_versions: 2147483647, population: changesPopulation(),
  }).kind, "ran", "the producer's own maximum was refused");
  // Fail closed, exactly as a malformed shape does: no rows, no count, no absence claim.
  const projected = projectGovernedEmptiness("in_force_on", [{
    envelope: { status: "ok", publisher: "lu-legilux" }, works: [{ work: "a" }],
    total_works_in_force: 1e20, population: inForcePopulation(),
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
    changes: [], works_changed: 7, new_versions: 7, population: changesPopulation(),
  }).kind, "ran");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok", publisher: "eu-eurlex" },
    works: [], total_works_in_force: 412, population: inForcePopulation(),
  }).kind, "ran");
  // And the count still reaches the caller rather than being discarded.
  const partition = partitionGovernedResponse("in_force_on", [
    { envelope: { status: "ok", publisher: "eu-eurlex" }, works: [],
      total_works_in_force: 412, population: inForcePopulation() },
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
    population: inForcePopulation(),
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
  // O17, AND THIS LINE USED TO ASSERT THE OPPOSITE. It required a search terminal object to be
  // terminal for in_force_on too, which is the defect: `McpCore.CallToolCore` stamps
  // `["tool_called"] = name`, so this object answered a different question, and honouring it
  // here tells the reader the corpus holds nothing for the thing they actually asked about.
  // An absence asserted about the wrong subject is the worst failure this surface has.
  assert.equal(classifyEnvelope("in_force_on", noCorpus).kind, "invalid");
  const projected = projectSearch([noCorpus]);
  assert.equal(projected.absence, "no_corpus");
  assert.equal(searchEmptyPresentation("no_corpus").sentence, NO_CORPUS_SENTENCE);
  assert.ok(!NO_CORPUS_SENTENCE.toLowerCase().includes("try"),
    "retrying cannot help, so the copy never suggests it");
  // Same object, wrong operation: the in-force surface must not print the corpus sentence from
  // a terminal answer to a search. It claims nothing instead.
  assert.equal(projectGovernedEmptiness("in_force_on", [noCorpus], 0).empty,
    "incomplete_response");
  assert.equal(
    projectGovernedEmptiness("in_force_on", [{ ...noCorpus, tool_called: "in_force_on" }], 0)
      .empty,
    "no_corpus", "the terminal state was lost for the operation it actually answered");
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
    ["mode_unavailable", modeUnavailable("lu-legilux")],
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
    modeUnavailable("lu-legilux"),
  ]);
  assert.equal(modeOnly.limitations.length, 0, "no capability limitation exists");
  // PINNED, not merely excluded. This line was `assert.notEqual(..., "all_refused")`, which is
  // one forbidden value out of eight: it passed for the other seven, and the one it actually
  // shipped was `incomplete_response`, whose sentence told the reader the response could not be
  // read and to send it again. A check that cannot fail in the direction that matters is not a
  // check, and the state it was written to defend never existed until now.
  assert.equal(modeOnly.absence, "retrieval_mode_unavailable",
    "all_refused selects the filter-refusal copy and no filter was refused");
  assert.ok(modeOnly.modeUnavailable?.includes("lu-legilux"));

  // A real capability refusal still reaches all_refused.
  assert.equal(projectSearch([refused("lu-legilux", ["domain"])]).absence, "all_refused");
});

// ---------------------------------------------------------------------------
// A complete response saying no index can run this mode (the per-deployment shape)
// ---------------------------------------------------------------------------

test("an all-mode-unavailable search states the mode, never that the response was unreadable", () => {
  // THE SHIPPED DEFECT, and it is not a rare envelope: `HybridReady` is
  // `_encoder is not null && _vectors is not null`, so the repo-built container and every build
  // without semantic vectors answers exactly this for EVERY hybrid search. The producer's side
  // is coherent throughout: one unit per publisher, `hits: []`, a population declaring
  // `query_ran: false`, and receipts that reconcile. The client called it `incomplete_response`
  // and printed two false things at once, that the response was incomplete and that retrying
  // might change it.
  const modeOnly = projectSearch([modeUnavailable("lu-legilux"), modeUnavailable("eu-eurlex")]);
  assert.equal(modeOnly.absence, "retrieval_mode_unavailable");

  // THE SENTENCE, through the same call Search.tsx makes. The state name is not the product;
  // a right state mapped to the wrong copy is this defect one layer up.
  const shown = searchEmptyPresentation(modeOnly.absence);
  assert.equal(shown.kind, "retrieval_mode_unavailable");
  assert.equal(shown.sentence, RETRIEVAL_MODE_UNAVAILABLE_SENTENCE);
  assert.equal(shown.sentence,
    "Words + meaning is not available on any selected publisher's index, so this query did not "
    + "run and Lex cannot state what is absent. Choose Exact words to run the same search.");

  // The two claims the shipped copy made, refused by name rather than by state.
  assert.ok(!shown.sentence.includes("incomplete"),
    "the page still calls a complete, coherent response incomplete");
  assert.ok(!/\bagain\b/.test(shown.sentence),
    "the page still asks for a retry that returns the identical answer");
  // And it is not a corpus claim either: nothing was measured.
  assert.ok(!shown.sentence.includes("corpus"));
  // It names the control that does change the outcome, spelled as the button's own label.
  assert.ok(shown.sentence.includes("Exact words"));

  // Neither of the two states one branch either side of it, both of which are false here.
  assert.notEqual(shown.sentence, INCOMPLETE_RESPONSE_SENTENCE);
  assert.notEqual(shown.sentence, "No selected publisher ran this query.");

  // The count beside it is not authoritative: nothing ran, so a bare "0 laws" would assert an
  // absence measured by no query at all. This is the half of the repair that a new state added
  // to the union without a clause here would have silently lost.
  assert.equal(absenceAuthorityIncomplete(modeOnly.absence), true);
});

test("a publisher that ran beside one that lacked the mode still speaks only for what ran", () => {
  // THE MIX. One publisher executed and matched nothing; the other never ran. The response
  // measured a real population, so it may speak, but only for the publisher that could apply
  // the query: `mixed_no_match`, exactly as a filter refusal beside a ran unit does.
  const mixed = projectSearch([modeUnavailable("eu-eurlex"), searchOk("lu-legilux", 0)]);
  assert.equal(mixed.absence, "mixed_no_match",
    "a unit that ran was swallowed by the mode-unavailable state");
  assert.equal(searchEmptyPresentation(mixed.absence).sentence, MIXED_ZERO_SENTENCES.search);

  // And with hits, the mode-unavailable sibling costs nothing but the disclosure.
  const withHits = projectSearch([modeUnavailable("eu-eurlex"), searchOk("lu-legilux", 2)]);
  assert.equal(withHits.absence, "has_results");
  assert.ok(withHits.modeUnavailable?.includes("eu-eurlex"));

  // A genuine refusal beside a mode-unavailable sibling keeps its coverage sentence, because a
  // typed filter limitation really is there to render. The new state is for the case where the
  // retrieval mode is the whole story, not a blanket capture of every unit that did not run.
  const refusedToo = projectSearch([modeUnavailable("eu-eurlex"), refused("lu-legilux", ["domain"])]);
  assert.equal(refusedToo.absence, "all_refused");
  assert.equal(refusedToo.limitations.length, 1);
});

test("a genuinely unreadable response still reports incomplete and asks for a retry", () => {
  // THE PATH THIS REPAIR MUST NOT WEAKEN. A response that really cannot be read keeps the
  // incomplete sentence, retry included, because there a retry is the honest advice.
  const unreadable = projectSearch([{ envelope: { status: "made_up" }, hits: [] }]);
  assert.equal(unreadable.absence, "incomplete_response");
  assert.equal(searchEmptyPresentation(unreadable.absence).sentence, INCOMPLETE_RESPONSE_SENTENCE);
  assert.ok(INCOMPLETE_RESPONSE_SENTENCE.includes("incomplete"));
  assert.ok(/\bagain\b/.test(INCOMPLETE_RESPONSE_SENTENCE));

  // An unreadable sibling BESIDE a mode-unavailable unit is unreadable first. The mode state is
  // only for a response every unit of which is coherent, so it can never absorb an invalid one.
  const poisoned = projectSearch([modeUnavailable("lu-legilux"), { envelope: null }]);
  assert.equal(poisoned.absence, "incomplete_response",
    "an invalid sibling was absorbed into the retrieval-mode state");

  // The empty response claims nothing either, and is not the mode state.
  assert.equal(projectSearch([]).absence, "incomplete_response");
});

test("the retrieval-mode state cannot arise on a tool that has no retrieval mode", () => {
  // WHY NO NEW GOVERNED VARIANT WAS ADDED, proved rather than asserted. `requiresRetrievalMode`
  // is true for search alone, so `retrieval_mode_unavailable` on changes_in_period or in_force_on
  // is not a mode claim at all: it is an unknown status, which is invalid, which is incomplete.
  // That is what keeps the `empty` union of eight-branch ternaries in App.tsx unchanged, and if
  // this ever stops holding those chains need the same audit search just had.
  for (const tool of ["changes_in_period", "in_force_on"]) {
    const raw = { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } };
    const partition = partitionGovernedResponse(tool, [raw]);
    assert.equal(partition.modeUnavailableCount, 0, `${tool} minted a mode claim`);
    assert.equal(partition.invalidCount, 1, `${tool} admitted an unknown status`);
    assert.equal(projectGovernedEmptiness(tool, [raw], 0).empty, "incomplete_response");
  }
  // The classifier agrees at the unit level, on the same status search calls a mode claim.
  assert.equal(classifyEnvelope("search",
    { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" },
      population: searchPopulationFor("retrieval_mode_unavailable") }).kind, "mode_unavailable");
  assert.equal(classifyEnvelope("changes_in_period",
    { envelope: { status: "retrieval_mode_unavailable", publisher: "lu-legilux" } }).kind,
    "invalid");
});

test("every search absence state has copy and a count verdict, with none defaulting", () => {
  // THE CHAIN THE BRIEF NAMES. A state added to the union without a clause in either consumer
  // is the confident branch: the widest corpus claim from the presentation, and the bare
  // authoritative count from the header. Both are switches with `assertNever` now, so this test
  // is a live inventory rather than the guard itself, and it fails the day the union grows
  // without this list growing with it.
  const states = ["has_results", "partial_results", "all_refused", "no_corpus", "mixed_no_match",
    "no_match", "incomplete_response", "retrieval_mode_unavailable"] as const;
  const corpusWide = "Nothing in the corpus matches that.";
  for (const state of states) {
    const shown = searchEmptyPresentation(state);
    assert.ok(shown.sentence.length > 0, `${state} rendered no sentence`);
    // Only the three states that measured a population may reach the widest claim.
    if (shown.sentence === corpusWide) {
      assert.ok(["has_results", "partial_results", "no_match"].includes(state),
        `${state} reached the whole-corpus absence claim`);
    }
    assert.equal(typeof absenceAuthorityIncomplete(state), "boolean");
  }
  // The verdicts the header depends on, pinned so a silent flip is a failure and not a shrug.
  assert.deepEqual(states.filter((state) => absenceAuthorityIncomplete(state)),
    ["partial_results", "mixed_no_match", "incomplete_response", "retrieval_mode_unavailable"]);
});

test("the search surface renders the empty sentence and the count verdict through the seams", () => {
  // STRUCTURAL, on the precedent of the withholding guard below, because no node test can import
  // a .tsx component. It is what makes the sentence assertions above claims about production
  // rather than about a function the component might have stopped calling. Comments are stripped
  // first, so a comment explaining the defect is not read as a reintroduction of it.
  const source = readFileSync(new URL("./Search.tsx", import.meta.url), "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/^[ \t]*\/\/.*$/gm, "");
  assert.ok(source.includes("searchEmptyPresentation(results.absence)"),
    "the search surface maps its own state to copy again");
  assert.ok(source.includes("absenceAuthorityIncomplete(results.absence)"),
    "the count authority is back to a chain that answers false for anything unlisted");
  assert.ok(!/results\.absence === "partial_results"\s*\n?\s*\|\|/.test(source),
    "the authority chain the exhaustive switch replaced is back");
  // The mode-unavailable reader is not handed the filter-capability explanation, and not the
  // textless-versions fallback either: both name a cause this response never established.
  const branchOpen = 'results.absence === "retrieval_mode_unavailable" ? (';
  assert.ok(source.includes(branchOpen),
    "the mode-unavailable branch of the empty-state sub-copy is gone");

  // AND THE PROSE INSIDE IT, not merely the branch. Found by mutation: rewriting this paragraph
  // to "The response was incomplete and could not be read. Try the request again." left the
  // whole suite green, because every assertion above pins the empty SENTENCE and this is the
  // paragraph directly beneath it on the page. Both claims the repair exists to remove could be
  // reintroduced one line lower than everything that was watching for them.
  const start = source.indexOf(branchOpen) + branchOpen.length;
  const branch = source.slice(start, source.indexOf(") : (", start));
  assert.ok(branch.length > 0 && branch.length < 600, "the sub-copy branch could not be read");
  assert.ok(!/\bincomplete\b/i.test(branch),
    "the sub-copy calls a complete, coherent response incomplete again");
  assert.ok(!/\bagain\b/i.test(branch),
    "the sub-copy asks for a retry that returns the identical answer again");
  assert.ok(!branch.includes("LIMITATION_EXPLANATION"),
    "the sub-copy blames a filter that was never refused");
  // It states the true thing instead: a fact about the mounted index, not about the law.
  assert.ok(branch.includes("mounted index"),
    "the sub-copy stopped scoping its claim to the index");
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
    population: searchPopulationFor(LIMITATION_STATUS),
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
    modeUnavailable("eu-eurlex"),
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

  // THE FIXTURE WAS THINNER THAN THE PRODUCER, and its counts said so. It carried
  // `works_changed: 1` on a changes REFUSAL, which `McpCore` 1779 mints as a hard-coded
  // `refusal["works_changed"] = 0` immediately after the refusal object is built. So the smuggled
  // row was being carried by a unit the producer cannot emit, and the assertion beneath it pinned
  // `all_refused`, a coverage sentence reading "No selected publisher ran this query." beside a
  // unit reporting that a work had changed. The subject of this test is row authority and it is
  // unchanged; only the forgery is gone, and it now has a test of its own that refuses it rather
  // than an expectation that believed it.
  const changes = projectGovernedEmptiness("changes_in_period", [
    { ...refused("eu-eurlex", ["domain"], "changes_in_period"),
      changes: [{ work: "smuggled" }], works_changed: 0, new_versions: 0 },
  ], 0);
  assert.deepEqual(changes.partition.ran, []);
  assert.equal(changes.empty, "all_refused");

  const inForce = projectGovernedEmptiness("in_force_on", [
    { ...refused("lu-legilux", ["hierarchy"], "in_force_on"), works: [{ work: "smuggled" }],
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
    [refused("lu-legilux", ["domain"], "changes_in_period")], 0).empty, "all_refused");
  assert.equal(projectGovernedEmptiness("changes_in_period",
    [refused("eu-eurlex", ["domain"], "changes_in_period"), changesOk("lu-legilux", 0)],
    0).empty, "mixed_no_match");
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

/**
 * A coherent refusal for one publisher, on any governed tool. The tool is now load-bearing:
 * search attaches a population to its refusal and the other two do not, so one fixture cannot
 * stand for all three.
 */
const refusalFor = (publisher: string, tool = "search") =>
  refused(publisher, ["domain"], tool);

/** The coherent success fixture for each governed tool, keyed by tool. */
const ranFor: Record<string, (publisher: string, rows: number) => unknown> = {
  search: (publisher, rows) => searchOk(publisher, rows),
  changes_in_period: (publisher, rows) => changesOk(publisher, rows),
  in_force_on: (publisher, rows) => inForceOk(publisher, rows),
};

test("ran beside refused for one publisher withholds both, on every governed tool", () => {
  for (const tool of Object.keys(ranFor)) {
    const ranEntry = ranFor[tool]!("lu-legilux", 2);
    const refusal = refusalFor("lu-legilux", tool);
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
  const mode = modeUnavailable("lu-legilux");
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
  const mode = modeUnavailable("lu-legilux");
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
      [ranFor[tool]!("lu-legilux", 2), refusalFor("eu-eurlex", tool)]);
    assert.deepEqual(partition.conflictedPublishers, [], `${tool}: invented a conflict`);
    assert.equal(partition.ran.length, 1, `${tool}: dropped an uncontradicted publisher`);
    assert.equal(partition.limitations.length, 1, `${tool}: dropped a real limitation`);
    assert.equal(partition.invalidCount, 0, `${tool}: reported a coherent response as partial`);
  }
});

test("a lone genuine refusal still reaches all_refused, on every governed tool", () => {
  // The other guard: this is the case the copy about coverage exists for, and it must survive.
  for (const tool of Object.keys(ranFor)) {
    const partition = partitionGovernedResponse(tool, [refusalFor("lu-legilux", tool)]);
    assert.deepEqual(partition.conflictedPublishers, []);
    assert.equal(partition.limitations.length, 1, `${tool}: silenced a lone refusal`);
    assert.equal(partition.allRefused, true, `${tool}: lost the coverage state`);
  }
  assert.equal(projectSearch([refusalFor("lu-legilux")]).absence, "all_refused");
  assert.equal(
    projectGovernedEmptiness("in_force_on", [refusalFor("lu-legilux", "in_force_on")], 0).empty,
    "all_refused");
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
  const alias = classifyEnvelope("search", modeUnavailable("LU-Legilux"));
  assert.equal(alias.kind, "mode_unavailable");
  assert.equal(alias.kind === "mode_unavailable" && alias.publisher, undefined,
    "a case alias was named as an unavailable publisher");
  const padded = classifyEnvelope("search", modeUnavailable(" lu-legilux "));
  assert.equal(padded.kind === "mode_unavailable" && padded.publisher, undefined);
  const real = classifyEnvelope("search", modeUnavailable("lu-legilux"));
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
    retrieval_mode: "keyword", population: searchPopulationFor("ok"),
  }).kind, "ran");
  assert.equal(classifyEnvelope("in_force_on", {
    envelope: { status: "ok" }, works: [{ lex_id: "lu:w1:2024-01-01" }],
    total_works_in_force: 1, population: inForcePopulation(),
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
    population: inForcePopulation(),
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
    population: inForcePopulation(),
  };
  const decision = projectGovernedEmptiness("in_force_on", [mixed], 1);
  assert.equal(decision.empty, null, "a normal row exists, so this is a result");
  assert.equal(decision.ambiguous.length, 1, "the ambiguity unit reaches the caller");
  assert.equal((decision.ambiguous[0] as { work: string }).work, "w2");

  // And the invalid-sibling disclosure survives the ambiguity-only branch.
  const ambiguousPlusInvalid = projectGovernedEmptiness("in_force_on", [
    { envelope: { status: "ambiguous_version", publisher: "lu-legilux" }, works: [],
      ambiguous_works: [{ work: "w2", valid_from: "2024-01-01" }],
      total_works_in_force: 1, population: inForcePopulation() },
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
    population: searchPopulationFor(LIMITATION_STATUS),
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

// ---------------------------------------------------------------------------
// O20: the conflicted-publisher list is transport data, so the sentence validates it
// ---------------------------------------------------------------------------
//
// `UiEffect` carries this field from the assistant as well as from the local projector. It was
// typed `string[]` and handed straight to `length` and `join` with no runtime check, and a type
// annotation is erased at runtime. The validation lives in the function rather than at the call
// site so that every present and future caller is covered.

test("a hostile conflicted-publisher payload names nobody and never throws", () => {
  const hostile: [string, unknown][] = [
    // THE TRAP, and the case a reader dismisses as impossible. A string HAS a `length`, so a
    // guard written against emptiness admits it and everything after walks the CHARACTERS.
    // publisherIdentity accepts /^[a-z0-9_-]+$/ from 1 to 64 characters, so "e" and "u" are both
    // legal identities and the sentence renders "Nothing from e, u is shown here."
    ["non-empty string", "eu"],
    ["longer string", "lu-legilux"],
    // An array-like object walks the same path for the same reason.
    ["array-like object", { 0: "lu-legilux", length: 1 }],
    ["plain object", { publishers: ["lu-legilux"] }],
    ["null", null],
    ["undefined", undefined],
    ["number", 2],
    ["boolean", true],
    ["array with a non-string member", ["lu-legilux", 7]],
    ["array with a null member", ["lu-legilux", null]],
    ["array with an undefined member", ["lu-legilux", undefined]],
    ["array with a padded member", ["lu-legilux", " eu-eurlex "]],
    ["array with an upper-case member", ["LU-Legilux"]],
    ["array with an empty member", [""]],
    ["array with the ? sentinel", ["?"]],
    ["nested array", [["lu-legilux"]]],
    ["duplicate", ["lu-legilux", "lu-legilux"]],
    // Five valid names, one past the cap. Two publishers are registered.
    ["over-cap array", ["pub-1", "pub-2", "pub-3", "pub-4", "pub-5"]],
    ["overlong member", ["x".repeat(65)]],
  ];
  for (const [label, value] of hostile) {
    let sentence: string | undefined = "unset";
    assert.doesNotThrow(() => { sentence = conflictedPublishersSentence(value); },
      `${label} threw instead of failing closed`);
    assert.equal(sentence, undefined, `${label} produced a name sentence`);
  }
});

test("validation cannot pass by refusing everything", () => {
  // The liveness half. A guard that returns undefined unconditionally satisfies every hostile
  // case above and destroys the disclosure the sentence exists for.
  const two = conflictedPublishersSentence(["eu-eurlex", "lu-legilux"]);
  assert.ok(two !== undefined, "a legitimate two-publisher list was refused");
  assert.ok(two.includes("eu-eurlex") && two.includes("lu-legilux"), "both names must appear");
  assert.ok(conflictedPublishersSentence(["lu-legilux"]) !== undefined,
    "a legitimate one-publisher list was refused");
  // The cap is a bound, not a narrowing: exactly cap-many valid distinct names still speak.
  assert.ok(conflictedPublishersSentence(["pub-1", "pub-2", "pub-3", "pub-4"]) !== undefined,
    "the cap refused a list at its own maximum");
  // Underscore is inside the producer's declared class, so it is not hostile input.
  assert.ok(conflictedPublishersSentence(["lu_legilux"]) !== undefined,
    "a grammar the producer declares was refused");
});

test("a locally derived partition value passes the guard unchanged", () => {
  // The guard must not refuse the projector's own output, which is where this field normally
  // comes from: sorted, distinct, already validated publisher ids.
  const partition = partitionGovernedResponse("search", [
    searchOk("lu-legilux", 2), refusalFor("lu-legilux"),
    searchOk("eu-eurlex", 1), refusalFor("eu-eurlex"),
  ]);
  assert.deepEqual(partition.conflictedPublishers, ["eu-eurlex", "lu-legilux"],
    "the fixture stopped producing two conflicted publishers");
  const sentence = conflictedPublishersSentence(partition.conflictedPublishers);
  assert.ok(sentence !== undefined, "the guard refused the projector's own list");
  assert.ok(sentence.includes("eu-eurlex") && sentence.includes("lu-legilux"));
  // And a one-publisher partition, the ordinary case, is untouched too.
  const single = partitionGovernedResponse("in_force_on",
    [inForceOk("lu-legilux", 2), refusalFor("lu-legilux", "in_force_on")]);
  assert.ok(conflictedPublishersSentence(single.conflictedPublishers) !== undefined);
});

test("failing closed removes the names, never the incompleteness disclosure", () => {
  // The two disclosures are independent by construction, and this pins that. views.tsx renders
  // PARTIAL_RESPONSE_SENTENCE whenever `partial` is set and only the inner named paragraph from
  // this sentence, so a corrupted name list must cost the reader the names and nothing else.
  const decision = projectGovernedEmptiness("in_force_on",
    [inForceOk("lu-legilux", 2), refusalFor("lu-legilux", "in_force_on")], 0);
  assert.equal(decision.partial, true, "the disclosure the caller renders was lost");
  assert.equal(decision.empty, "incomplete_response");
  assert.ok(PARTIAL_RESPONSE_SENTENCE.length > 0 && INCOMPLETE_RESPONSE_SENTENCE.length > 0,
    "the generic notices are what survive a hostile name list");
  // Same response, names corrupted on the wire: no names, and the partition is untouched.
  assert.equal(conflictedPublishersSentence("lu-legilux"), undefined);
  assert.equal(conflictedPublishersSentence({ length: 1 }), undefined);
  assert.equal(decision.partial, true, "validating the names changed the partition");
  // The honest list from that very same partition still names its publisher.
  assert.ok(conflictedPublishersSentence(decision.partition.conflictedPublishers) !== undefined,
    "failing closed on hostile input also silenced the honest case");
});

// ---------------------------------------------------------------------------
// O17: the terminal object names the operation it answered
// ---------------------------------------------------------------------------

test("a terminal object is read only for the operation it says it answered", () => {
  // `McpCore.CallToolCore` line 901 stamps `["tool_called"] = name`. Four ways to fail, because
  // they are four different lies, and each is exercised on its own so a repair that closes one
  // cannot pass for closing all four.
  const terminal = (over: Record<string, unknown> = {}) => ({
    status: NO_CORPUS_STATUS,
    detail: "This server started with zero verified indexes, so it holds no law.",
    hosted_endpoint: "https://law.soufien.lu/mcp",
    tool_called: "in_force_on",
    ...over,
  });
  // The operation it actually answered.
  assert.equal(classifyEnvelope("in_force_on", terminal()).kind, "no_corpus");
  // 1. Missing.
  const { tool_called: _omitted, ...missing } = terminal();
  assert.equal(classifyEnvelope("in_force_on", missing).kind, "invalid",
    "a terminal object naming no operation authorized the corpus sentence");
  // 2. Wrong type: not a bounded identifier at all.
  for (const wrong of [7, null, {}, [], true, "", "x".repeat(65), "in force on"]) {
    assert.equal(classifyEnvelope("in_force_on", terminal({ tool_called: wrong })).kind,
      "invalid", `tool_called ${JSON.stringify(wrong)} was read as an operation`);
  }
  // 3. A bounded identifier naming no governed operation. "IN_FORCE_ON" belongs here rather
  // than under wrong type: the identifier grammar is case-insensitive and the tool set is not,
  // which is the same split publisherIdentity draws.
  for (const unknown of ["coverage", "as_of", "IN_FORCE_ON", "in_force_on_"]) {
    assert.equal(classifyEnvelope("in_force_on", terminal({ tool_called: unknown })).kind,
      "invalid", `tool_called ${unknown} was read as this operation`);
  }
  // 4. A governed operation, but not this one. THE CASE THAT MATTERS: the reader is told the
  // corpus holds nothing about what they asked, on the strength of an answer to something else.
  for (const other of ["search", "changes_in_period"]) {
    assert.equal(classifyEnvelope("in_force_on", terminal({ tool_called: other })).kind,
      "invalid", `an answer to ${other} spoke for in_force_on`);
  }
  // And the whole-surface consequence, not only the classification.
  assert.equal(
    projectGovernedEmptiness("in_force_on", [terminal({ tool_called: "search" })], 0).empty,
    "incomplete_response", "a search terminal object printed the corpus sentence on in-force");
  assert.equal(projectGovernedEmptiness("in_force_on", [terminal()], 0).empty, "no_corpus");
});

// ---------------------------------------------------------------------------
// The corrected scope rule: unreadable invalidates the claim that carries it
// ---------------------------------------------------------------------------

test("an unreadable required scope invalidates the whole claim, not only the scope", () => {
  // `parseScope` always detected these. What it did was write the verdict into a field and let
  // the unit go on authorizing rows, a denominator, a limitation, a mode and an absence claim,
  // while reporting the response as fully usable. A marker that looks like a check while the
  // guarantee it implies happens nowhere is the defect class this table exists to remove.
  const cases: [string, unknown][] = [
    // Absent, where the producer publishes one on every path.
    ["search absent", { envelope: { status: "ok", publisher: "lu-legilux" },
      retrieval_mode: "keyword", hits: [{ lex_id: "lu-legilux:w0" }] }],
    ["in_force absent", { envelope: { status: "ok", publisher: "lu-legilux" },
      works: [{ work: "a" }], total_works_in_force: 1 }],
    ["changes absent", { envelope: { status: "ok", publisher: "lu-legilux" },
      changes: [{ work: "w" }], works_changed: 1, new_versions: 1 }],
    // Malformed.
    ["in_force not an object", { envelope: { status: "ok", publisher: "lu-legilux" },
      works: [{ work: "a" }], total_works_in_force: 1, population: "1250" }],
    // Out of the producer's Int32 range.
    ["in_force overflow", { envelope: { status: "ok", publisher: "lu-legilux" },
      works: [{ work: "a" }], total_works_in_force: 1,
      population: { ...inForcePopulation(), works_covered: 2147483648 } }],
    ["search overflow", { envelope: { status: "ok", publisher: "lu-legilux" },
      retrieval_mode: "keyword", hits: [{ lex_id: "lu-legilux:w0" }],
      population: { ...searchPopulationFor("ok"), works_in_scope: 2147483648 } }],
    // Internally incoherent: a population arriving where the producer publishes none.
    ["in_force refusal with a population",
      { ...refused("lu-legilux", ["domain"], "in_force_on"),
        population: inForcePopulation() }],
    ["changes refusal with a population",
      { ...refused("lu-legilux", ["domain"], "changes_in_period"),
        population: changesPopulation() }],
  ];
  const toolOf = (label: string) => label.startsWith("search")
    ? "search" : label.startsWith("changes") ? "changes_in_period" : "in_force_on";
  for (const [label, entry] of cases) {
    const tool = toolOf(label);
    assert.equal(classifyEnvelope(tool, entry).kind, "invalid",
      `${label}: an unreadable required scope still produced a unit`);
    const partition = partitionGovernedResponse(tool, [entry]);
    assert.deepEqual(partition.ran, [], `${label}: rows survived`);
    assert.equal(partition.limitations.length, 0, `${label}: a limitation survived`);
    assert.equal(partition.modeUnavailableCount, 0, `${label}: a mode claim survived`);
    assert.equal(partition.allRefused, false, `${label}: an absence claim survived`);
    assert.equal(partition.invalidCount, 1, `${label}: the response looked fully usable`);
  }
  // The publisher is NAMED rather than merely counted, so a surface can say whose claim went.
  const broken = { envelope: { status: "ok", publisher: "lu-legilux" },
    works: [{ work: "a" }], total_works_in_force: 1, population: "1250" };
  assert.deepEqual(parseGovernedResponse("in_force_on", [broken]).unreadable, ["lu-legilux"]);
  assert.equal(parseGovernedResponse("in_force_on", [broken]).scopeAuthority,
    "usable_units_only");
});

// ---------------------------------------------------------------------------
// O16: the producer's paging receipts, per tool
// ---------------------------------------------------------------------------

test("in_force_on truncation must be the arithmetic the producer performed", () => {
  // `["truncated"] = total > localOffset + pageUnits`, pageUnits = rows.Count + ambiguities.Count
  // (McpCore 1155-1157). Every term is on the entry, so this is an exact equality.
  const page = (over: Record<string, unknown>) => ({
    envelope: { status: "ok", publisher: "lu-legilux" },
    works: [{ work: "a" }],
    total_works_in_force: 5,
    population: inForcePopulation(),
    offset: 0,
    truncated: true,
    ...over,
  });
  assert.equal(classifyEnvelope("in_force_on", page({})).kind, "ran",
    "the producer's own receipt was refused");
  assert.equal(classifyEnvelope("in_force_on", page({ truncated: false })).kind, "invalid",
    "a page claiming to show everything beside a total of 5 and one row was accepted");
  assert.equal(
    classifyEnvelope("in_force_on", page({ total_works_in_force: 1, truncated: true })).kind,
    "invalid", "a truncation claim the counts contradict was accepted");
  assert.equal(
    classifyEnvelope("in_force_on", page({ total_works_in_force: 1, truncated: false })).kind,
    "ran");
  // Ambiguity units consume the page too, so they count toward pageUnits.
  assert.equal(classifyEnvelope("in_force_on", page({
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    ambiguous_works: [{ work: "b" }], total_works_in_force: 2, truncated: false,
  })).kind, "ran");
  assert.equal(classifyEnvelope("in_force_on", page({
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    ambiguous_works: [{ work: "b" }], total_works_in_force: 2, truncated: true,
  })).kind, "invalid", "the ambiguity unit was left out of the page it occupies");
  // Half a receipt is a shape nothing emits: the producer mints both in one object literal.
  assert.equal(classifyEnvelope("in_force_on", page({ offset: undefined })).kind, "invalid");
  assert.equal(classifyEnvelope("in_force_on", page({ truncated: undefined })).kind, "invalid");
  // Bad types and out-of-range offsets fail closed rather than being ignored.
  for (const bad of ["0", -1, 1.5, 2147483648, Number.NaN]) {
    assert.equal(classifyEnvelope("in_force_on", page({ offset: bad })).kind, "invalid",
      `offset ${String(bad)} was accepted`);
  }
  assert.equal(classifyEnvelope("in_force_on", page({ truncated: "yes" })).kind, "invalid");
});

test("changes_in_period cardinality must equal the rows it shipped", () => {
  // `["shown"] = rows.Length` (McpCore 1813), then `response_row_set` is minted from `shown` and
  // `works_changed` in a second pass (1865-1882).
  const page = (over: Record<string, unknown>) => ({
    envelope: { status: "ok", publisher: "lu-legilux" },
    changes: [{ work: "w1" }],
    works_changed: 5,
    new_versions: 5,
    population: changesPopulation(),
    shown: 1,
    offset: 0,
    response_row_set: { maximum: 20, returned: 1, truncated: true },
    global_response_row_set: { offset: 0, maximum: 20, returned: 1, total: 5, truncated: true },
    ...over,
  });
  assert.equal(classifyEnvelope("changes_in_period", page({})).kind, "ran",
    "the producer's own receipts were refused");
  assert.equal(classifyEnvelope("changes_in_period", page({ shown: 2 })).kind, "invalid",
    "a page claiming to show two rows shipped one");
  assert.equal(classifyEnvelope("changes_in_period", page({ shown: 0 })).kind, "invalid");
  // ISOLATED from the row receipt. With `response_row_set` present, a wrong `shown` is also
  // caught by `returned !== shown`, so removing the `shown` check alone killed no test. The
  // producer stamps `shown` on the entry itself, so it has to stand on its own.
  const { response_row_set: _receipt, ...noReceipt } = page({});
  assert.equal(classifyEnvelope("changes_in_period", noReceipt).kind, "ran",
    "a page with no row receipt stopped being readable");
  assert.equal(
    classifyEnvelope("changes_in_period", { ...noReceipt, shown: 2 }).kind, "invalid",
    "a cardinality claim was only ever checked through a receipt that may be absent");
  assert.equal(
    classifyEnvelope("changes_in_period", { ...noReceipt, shown: "1" }).kind, "invalid");
  assert.equal(classifyEnvelope("changes_in_period", page({
    response_row_set: { maximum: 20, returned: 2, truncated: true },
  })).kind, "invalid", "the row receipt disagreed with the rows");
  assert.equal(classifyEnvelope("changes_in_period", page({
    response_row_set: { maximum: 20, returned: 1, truncated: false },
  })).kind, "invalid", "a page of 1 from a total of 5 claimed to be complete");
  assert.equal(classifyEnvelope("changes_in_period", page({
    works_changed: 1, response_row_set: { maximum: 20, returned: 1, truncated: false },
    global_response_row_set: { offset: 0, maximum: 20, returned: 1, total: 1,
                               truncated: false },
  })).kind, "ran", "a genuinely complete page was refused");
  assert.equal(classifyEnvelope("changes_in_period", page({
    global_response_row_set: { offset: 0, maximum: 20, returned: 1, total: 5,
                               truncated: false },
  })).kind, "invalid", "a global receipt contradicting its own three numbers was accepted");
  assert.equal(classifyEnvelope("changes_in_period", page({
    global_response_row_set: { offset: 3, maximum: 20, returned: 1, total: 5, truncated: true },
  })).kind, "invalid", "two readings of one request offset disagreed");
  // All or nothing on the entry-level pair.
  assert.equal(classifyEnvelope("changes_in_period", page({ shown: undefined })).kind,
    "invalid");
  assert.equal(classifyEnvelope("changes_in_period", page({ offset: undefined })).kind,
    "invalid");
});

test("the publisher receipt must agree with its own three numbers, on every tool", () => {
  // `MarkPublisherSet` (McpCore 701-712) stamps every item of all three tools and derives both
  // `returned` and `truncated` from `total` and one constant.
  const receipt = (over: Record<string, unknown> = {}) => ({
    total: 2, returned: 2, maximum: 8, truncated: false, ...over,
  });
  const withReceipt = (tool: string, publisher_result_set: unknown) => {
    const base = tool === "search"
      ? searchOk("lu-legilux", 1)
      : tool === "changes_in_period" ? changesOk("lu-legilux", 1) : inForceOk("lu-legilux", 1);
    return { ...base, publisher_result_set };
  };
  // COLLECTED, not asserted one at a time. A loop of `assert.equal` stops at the first tool, so
  // a mutation of the shared receipt reader reported only `search` and said nothing about
  // whether the other two were covered. Gathering the verdicts makes one mutation name all
  // three, which is what mutation-testing each tool separately actually requires.
  const probes: [string, unknown][] = [
    ["the producer's own receipt", receipt()],
    ["a receipt that miscounts itself", receipt({ returned: 1 })],
    ["a truncation claim of 2 out of a maximum of 8", receipt({ truncated: true })],
    ["a genuine publisher truncation", receipt({ total: 9, returned: 8, truncated: true })],
    ["a count the producer cannot mint", receipt({ total: "2" })],
    ["a truthy stand-in for a boolean", receipt({ truncated: 0 })],
    ["an array", []],
  ];
  const expected = ["ran", "invalid", "invalid", "ran", "invalid", "invalid", "invalid"];
  const tools = ["search", "changes_in_period", "in_force_on"];
  const verdicts = Object.fromEntries(tools.map((tool) =>
    [tool, probes.map(([, value]) => classifyEnvelope(tool, withReceipt(tool, value)).kind)]));
  assert.deepEqual(verdicts,
    Object.fromEntries(tools.map((tool) => [tool, expected])),
    `probes in order: ${probes.map(([label]) => label).join("; ")}`);
});

// ---------------------------------------------------------------------------
// O12: a page slice is not an absence
// ---------------------------------------------------------------------------

test("a positive total with nothing on the page never claims the corpus is empty", () => {
  // The response says there are five states and shows none of them, because one shared
  // remainingLimit paged this publisher out. Reporting "no publisher state covers that date"
  // beside it is a confident absence the response itself contradicts.
  const pagedOut = {
    envelope: { status: "ok", publisher: "lu-legilux" },
    works: [],
    total_works_in_force: 5,
    population: inForcePopulation(),
  };
  assert.equal(classifyEnvelope("in_force_on", pagedOut).kind, "ran",
    "the fixture stopped being a legitimate paged response");
  const decision = projectGovernedEmptiness("in_force_on", [pagedOut], 0);
  assert.equal(decision.partition.moreBeyondPage, true);
  assert.notEqual(decision.empty, "none_matched",
    "a page slice was presented as a statement that nothing matched");
  assert.equal(decision.empty, "incomplete_response");

  // The same for changes_in_period, whose rows are a slice of one globally merged page.
  const outranked = {
    envelope: { status: "ok", publisher: "lu-legilux" },
    changes: [], works_changed: 7, new_versions: 7, population: changesPopulation(),
  };
  const changes = projectGovernedEmptiness("changes_in_period", [outranked], 0);
  assert.equal(changes.partition.moreBeyondPage, true);
  assert.equal(changes.empty, "incomplete_response");

  // A mixed no-match is an absence claim too, so it is overridden by the same fact.
  const mixed = projectGovernedEmptiness("changes_in_period",
    [outranked, refused("eu-eurlex", ["domain"], "changes_in_period")], 0);
  assert.equal(mixed.empty, "incomplete_response",
    "a scoped absence sentence still spoke for a page that showed nothing of five");

  // AND THE HONEST ABSENCE SURVIVES, which is the guard against over-correction: a publisher
  // that really found nothing still says so.
  const nothing = {
    envelope: { status: "no_result", publisher: "lu-legilux" },
    works: [], total_works_in_force: 0, population: inForcePopulation(),
  };
  const empty = projectGovernedEmptiness("in_force_on", [nothing], 0);
  assert.equal(empty.partition.moreBeyondPage, false);
  assert.equal(empty.empty, "none_matched", "a real absence stopped being stateable");
  // A truncation receipt is the second, independent way to know the page was a slice.
  const truncatedPage = {
    envelope: { status: "no_result", publisher: "lu-legilux" },
    works: [], total_works_in_force: 0, population: inForcePopulation(),
    offset: 0, truncated: false,
  };
  assert.equal(projectGovernedEmptiness("in_force_on", [truncatedPage], 0).empty,
    "none_matched", "an untruncated empty page stopped being an absence");
});

// ---------------------------------------------------------------------------
// Ambiguity is read only where the table declares the field
// ---------------------------------------------------------------------------

test("a stray ambiguity field on a tool that has none drives nothing", () => {
  // `ambiguous_works` is in_force_on's field and no other tool's: `McpCore` emits it only there.
  // The count used to be read off any ran entry of any tool, so a changes_in_period entry
  // carrying the field drove that whole surface to `ambiguous_only`, asking the reader to
  // choose an exact publisher version on a page of period changes. The rows inside it were
  // never validated either, because nothing on that tool's path looks at them.
  const strayed = {
    ...changesOk("lu-legilux", 1),
    ambiguous_works: [{ nonsense: true }, null],
  };
  assert.equal(classifyEnvelope("changes_in_period", strayed).kind, "ran",
    "a field this tool has no rule for made the entry malformed");
  const decision = projectGovernedEmptiness("changes_in_period", [strayed], 0);
  assert.equal(decision.partition.ambiguityUnits, 0,
    "a field the table does not declare for this tool was counted");
  assert.deepEqual(decision.ambiguous, [],
    "an unvalidated object was handed to the caller to render");
  assert.notEqual(decision.empty, "ambiguous_only");
  // And in_force_on, where the table DOES declare it, still reads it.
  const real = {
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    works: [], ambiguous_works: [{ work: "w1" }], total_works_in_force: 1,
    population: inForcePopulation(),
  };
  const inForce = projectGovernedEmptiness("in_force_on", [real], 0);
  assert.equal(inForce.partition.ambiguityUnits, 1);
  assert.equal(inForce.empty, "ambiguous_only");
});

// ---------------------------------------------------------------------------
// The quarantined retrieval mode, pinned at the unit seam
// ---------------------------------------------------------------------------

test("a quarantined retrieval mode reports unavailability and never falls back", () => {
  // The producer attaches a population to this path like every other search path:
  // `["population"] = SearchPopulation(reader, filter, scopeFiltersApplied: true,
  // queryRan: false)` at McpCore.cs:1385-1386, beside `McpStatus.RetrievalModeUnavailable` at
  // 1376. With it, the reader is told which publisher cannot honour meaning search and why.
  const quarantined = {
    envelope: { publisher: "eu-eurlex", jurisdiction: "EU",
                status: "retrieval_mode_unavailable" },
    requested_retrieval_mode: "hybrid",
    retrieval_unavailable_reason: "benchmark_gate_failed",
    population: searchPopulationFor("retrieval_mode_unavailable"),
    hits: [],
  };
  const projected = projectSearch([quarantined]);
  assert.ok(projected.modeUnavailable?.includes("eu-eurlex"),
    "the reader was not told which publisher cannot honour meaning search");
  assert.ok(projected.modeUnavailable?.includes("signed retrieval benchmark"),
    "the fixed explanation was lost");
  assert.equal(projected.modeUsed, undefined,
    "a mode the request never got was reported as the mode used");
  assert.equal(projected.limitations.length, 0, "no filter was refused");

  // WITHOUT the population the producer always sends, the entry is a shape the producer cannot
  // emit, so it authorizes nothing. What matters for the trust property is what happens next:
  // the surface claims NOTHING. It does not answer with keyword results, and it does not report
  // an absence. The reader loses the specific explanation and gains no false answer.
  const { population: _dropped, ...unstated } = quarantined;
  const degraded = projectSearch([unstated]);
  assert.equal(degraded.modeUsed, undefined, "a silent keyword fallback appeared");
  assert.deepEqual(degraded.works, [], "results were rendered for a mode that never ran");
  assert.equal(degraded.absence, "incomplete_response",
    "an unusable response made a claim about the corpus");
  assert.notEqual(degraded.absence, "no_match");
});

// ---------------------------------------------------------------------------
// O2: the producer's response-wide receipts, reconciled once
// ---------------------------------------------------------------------------

test("a row receipt counting rows the response did not carry forbids the absence claim", () => {
  // THE REVIEWER'S PROBE, and the reason O2 was raised. Every per-unit check passes: a valid `ok`
  // search envelope, a coherent population, zero hits, and a `response_row_set` whose own three
  // members are perfectly well formed. Only the response-wide arithmetic can see it, and the
  // sentence on the other side of it is the widest absence claim this product makes.
  const receipt = { maximum: 20, returned: 1, truncated: false };
  const forged = { ...searchOk("lu-legilux", 0), response_row_set: receipt };
  assert.equal(classifyEnvelope("search", forged).kind, "ran",
    "the fixture stopped classifying ran, so this test would prove nothing");
  assert.equal(parseGovernedResponse("search", [forged]).receipts.kind, "irreconcilable");
  const projected = projectSearch([forged]);
  assert.notEqual(projected.absence, "no_match",
    "a response whose own receipt reported a returned row claimed the corpus holds nothing");
  assert.equal(projected.absence, "incomplete_response");
  assert.equal(searchEmptyPresentation(projected.absence as "incomplete_response").sentence,
    INCOMPLETE_RESPONSE_SENTENCE);
  // The honest reading of the very same receipt still runs: one hit, one returned row.
  const honest = { ...searchOk("lu-legilux", 1), response_row_set: receipt };
  assert.equal(parseGovernedResponse("search", [honest]).receipts.kind, "reconciled");
  assert.equal(projectSearch([honest]).absence, "has_results");
});

test("a response-wide receipt must be the same object on every sibling", () => {
  // `MarkResponseRows` writes ONE object into every item of the output, so two units carrying
  // different ones did not come from one response. The `returned` sum cannot catch this: both
  // readings below add up against the rows, and only the sibling comparison sees the swap.
  const agree = { maximum: 20, returned: 2, truncated: false };
  const both = [
    { ...searchOk("lu-legilux", 1), response_row_set: agree },
    { ...searchOk("eu-eurlex", 1), response_row_set: agree },
  ];
  assert.equal(parseGovernedResponse("search", both).receipts.kind, "reconciled");
  const disagreeing = [
    both[0],
    { ...searchOk("eu-eurlex", 1),
      response_row_set: { maximum: 40, returned: 2, truncated: false } },
  ];
  assert.equal(parseGovernedResponse("search", disagreeing).receipts.kind, "irreconcilable");
  assert.equal(projectSearch(disagreeing).absence, "partial_results",
    "a response carrying two different receipts presented itself as complete");
  // Mixed presence is neither present nor absent: one loop stamps every item.
  assert.equal(parseGovernedResponse("search", [both[0], searchOk("eu-eurlex", 1)]).receipts.kind,
    "irreconcilable");
  // The publisher receipt is response-wide in exactly the same way (MarkPublisherSet 701-712), so
  // two units disagreeing about how many publishers answered is the same defect.
  assert.equal(parseGovernedResponse("search", [
    { ...searchOk("lu-legilux", 1),
      publisher_result_set: { total: 2, returned: 2, maximum: 8, truncated: false } },
    { ...searchOk("eu-eurlex", 1),
      publisher_result_set: { total: 3, returned: 3, maximum: 8, truncated: false } },
  ]).receipts.kind, "irreconcilable");
});

test("a receipt returning more rows than its own maximum is refused", () => {
  // `returned` is `limit - remaining` and `maximum` is that same `limit`. No publisher loop can
  // drive `remaining` below zero: search caps each publisher at `Math.Min(remainingResults, ...)`
  // and in_force_on pages its groups at `remainingLimit`. So this is not a big page, it is a
  // number the producer cannot mint.
  const over = { maximum: 2, returned: 5, truncated: false };
  assert.equal(
    classifyEnvelope("search", { ...searchOk("lu-legilux", 5), response_row_set: over }).kind,
    "invalid");
  assert.equal(
    classifyEnvelope("in_force_on", { ...inForceOk("lu-legilux", 5), response_row_set: over }).kind,
    "invalid");
  // MergeGlobalChanges adds at most `limit` items, so the global receipt takes the same bound.
  assert.equal(classifyEnvelope("changes_in_period", {
    ...changesOk("lu-legilux", 5),
    global_response_row_set: { offset: 0, maximum: 2, returned: 5, total: 5, truncated: false },
  }).kind, "invalid");
  // And the boundary the producer really does reach stays legal, or a full page would be refused.
  assert.equal(
    classifyEnvelope("search",
      { ...searchOk("lu-legilux", 5),
        response_row_set: { maximum: 5, returned: 5, truncated: false } }).kind,
    "ran");
});

test("a forged global total in changes_in_period fails closed", () => {
  // `total` is `totalAcrossPublishers`, the sum of every stamped unit's `works_changed`, and it is
  // the figure the report prints beside the rows. Nothing else in the response contradicts a
  // forged one, which is exactly why it has to be reconciled here.
  const globalFor = (total: number, returned: number) => ({
    offset: 0, maximum: 20, returned, total, truncated: total > returned,
  });
  const honest = [
    { ...changesOk("lu-legilux", 1), global_response_row_set: globalFor(2, 2) },
    { ...changesOk("eu-eurlex", 1), global_response_row_set: globalFor(2, 2) },
  ];
  assert.equal(parseGovernedResponse("changes_in_period", honest).receipts.kind, "reconciled");
  const forged = honest.map((entry) =>
    ({ ...entry, global_response_row_set: globalFor(900, 2) }));
  assert.equal(parseGovernedResponse("changes_in_period", forged).receipts.kind, "irreconcilable");
  assert.equal(projectGovernedEmptiness("changes_in_period", forged, 2).partial, true,
    "a report whose own total nothing supports presented itself as complete");
  // A refusal is an addend worth zero, which is what the producer stamps on it. Counting only ran
  // units would be right by luck here and wrong the moment a refusal claimed a change.
  const withRefusal = [
    { ...changesOk("lu-legilux", 1), global_response_row_set: globalFor(1, 1) },
    { ...refused("eu-eurlex", ["domain"], "changes_in_period"), works_changed: 0,
      global_response_row_set: globalFor(1, 1) },
  ];
  assert.equal(parseGovernedResponse("changes_in_period", withRefusal).receipts.kind,
    "reconciled");
  const loudRefusal = [withRefusal[0], { ...withRefusal[1], works_changed: 7 }];
  assert.equal(parseGovernedResponse("changes_in_period", loudRefusal).receipts.kind,
    "irreconcilable",
    "a publisher that refused to look reported seven changed works and was believed");
});

test("a changes global receipt counting rows the response did not carry forbids absence", () => {
  // The sibling of the forged-total case, and it was missing. The global receipt states two
  // numbers the response must support: `total`, which the report prints, and `returned`, which
  // says how many change rows this page actually carried. The total had a test; `returned` did
  // not, and dropping its equality left the whole suite green.
  //
  // It matters for the same reason the search row receipt does. A response whose own receipt says
  // a row was returned, carrying no rows, would otherwise render a confident absence about a page
  // slice, which is the false-absence path this reconciliation exists to close.
  const globalFor = (returned: number) => ({
    offset: 0, maximum: 20, returned, total: 2, truncated: false,
  });
  const honest = [
    { ...changesOk("lu-legilux", 1), global_response_row_set: globalFor(2) },
    { ...changesOk("eu-eurlex", 1), global_response_row_set: globalFor(2) },
  ];
  assert.equal(parseGovernedResponse("changes_in_period", honest).receipts.kind, "reconciled",
    "a coherent response was refused, so this test proves nothing about the incoherent one");
  const overcounted = honest.map((entry) =>
    ({ ...entry, global_response_row_set: globalFor(3) }));
  assert.equal(parseGovernedResponse("changes_in_period", overcounted).receipts.kind,
    "irreconcilable",
    "a receipt claimed a third change row the response never carried and was believed");
  assert.equal(projectGovernedEmptiness("changes_in_period", overcounted, 2).partial, true);
});


test("in_force_on counts its ambiguity units as returned rows", () => {
  // `remainingLimit -= rows.Count + ambiguities.Count`, so a page of one row and one ambiguity
  // unit reports two returned. Counting visible rows only would make every ambiguous page
  // irreconcilable, which takes a real answer off the screen rather than a false one.
  const ambiguous = {
    envelope: { status: "ambiguous_version", publisher: "lu-legilux" },
    works: [{ work: "w0" }],
    ambiguous_works: [{ work: "w1" }],
    total_works_in_force: 2,
    population: inForcePopulation(),
    response_row_set: { maximum: 20, returned: 2, truncated: false },
  };
  assert.equal(parseGovernedResponse("in_force_on", [ambiguous]).receipts.kind, "reconciled");
  assert.equal(parseGovernedResponse("in_force_on", [{
    ...ambiguous, response_row_set: { maximum: 20, returned: 1, truncated: false },
  }]).receipts.kind, "irreconcilable");
});

test("a response that stamps no receipt at all is still readable", () => {
  // ABSENCE IS TOLERATED, PRESENCE IS BINDING. The governed contract fixtures carry no receipts,
  // and a client that required them would refuse every response shape it has not seen a live
  // server produce. This is also the non-trivial baseline for the four tests above: without it,
  // an always-irreconcilable verdict would satisfy every one of them.
  const plain = [searchOk("lu-legilux", 1), searchOk("eu-eurlex", 0)];
  assert.equal(parseGovernedResponse("search", plain).receipts.kind, "reconciled");
  assert.equal(projectSearch(plain).absence, "has_results");
  assert.equal(projectSearch([searchOk("lu-legilux", 0)]).absence, "no_match",
    "an ordinary empty response stopped being able to say the corpus holds nothing");
});

test("a malformed receipt on a refusal costs the absence claim, not the refusal", () => {
  // A ran unit fails closed on a malformed receipt and disappears, because its rows and counts
  // are what the receipt is about. A refusal must NOT: the capability statement is the only thing
  // it came to make, and dropping it would present a coverage gap as silence. The response loses
  // the right to speak for coverage instead, and the limitation still renders.
  const refusal = { ...refused("lu-legilux", ["domain"]), response_row_set: { maximum: "20" } };
  const parsed = parseGovernedResponse("search", [refusal]);
  assert.equal(parsed.units.length, 1, "the refusal was dropped along with its receipt");
  assert.equal(parsed.receipts.kind, "irreconcilable");
  const partition = partitionOf(parsed);
  assert.equal(partition.limitations.length, 1, "the capability statement was lost");
  assert.equal(searchAbsenceState(partition, 0), "incomplete_response",
    "a response carrying a receipt nothing could read still spoke for coverage");
});

// ---------------------------------------------------------------------------
// O3: the withholding sentence states the cause the parse established
// ---------------------------------------------------------------------------

test("a withheld publisher is told the cause the parse actually established", () => {
  // One sentence used to speak for both causes, and it asserted the stronger one: every publisher
  // in the merged list was told it had answered this query in ways that contradict each other. A
  // conflict is decided on the unit COUNT, so two byte-identical units contradict each other in
  // no way at all; an unreadable scope is one answer this client could not read. Neither is what
  // the reader was being told.
  const conflict = normalizeSearchResponse(parseGovernedResponse("search",
    [searchOk("lu-legilux", 1), searchOk("lu-legilux", 1)]));
  assert.equal(conflict.complete, false);
  const conflicted = conflict.complete === false ? withholdingSentence(conflict.withheld) : "";
  assert.ok(conflicted.includes("lu-legilux"), "the withheld publisher was not named");
  assert.ok(conflicted.includes("more than one"), "the conflict cause was not stated");
  assert.ok(!conflicted.includes("contradict"),
    "two identical units were reported to the reader as contradicting each other");

  const scope = normalizeSearchResponse(parseGovernedResponse("search",
    [{ ...searchOk("lu-legilux", 1), population: "1250" }]));
  assert.equal(scope.complete, false);
  const unreadable = scope.complete === false ? withholdingSentence(scope.withheld) : "";
  assert.ok(unreadable.includes("scope Lex could not read"),
    "the unreadable-scope cause was not stated");
  assert.ok(!unreadable.includes("contradict"),
    "an unreadable scope was reported to the reader as a contradiction");
  assert.ok(!unreadable.includes("more than one"),
    "one unreadable answer was reported as several answers");
  // Two causes, two sentences. Equal strings would mean one cause had absorbed the other again.
  assert.notEqual(conflicted, unreadable);
});

test("the withholding sentence names several publishers and speaks of each of them", () => {
  const answer = normalizeSearchResponse(parseGovernedResponse("search", [
    searchOk("lu-legilux", 1), searchOk("lu-legilux", 1),
    { ...searchOk("eu-eurlex", 1), population: "1250" },
  ]));
  assert.equal(answer.complete, false);
  const sentence = answer.complete === false ? withholdingSentence(answer.withheld) : "";
  assert.ok(sentence.includes("lu-legilux"), "the conflicted publisher was not named");
  assert.ok(sentence.includes("eu-eurlex"), "the unreadable-scope publisher was not named");
  assert.ok(!sentence.includes("that publisher"),
    "a list of publishers was addressed in the singular");
  assert.equal(sentence.split("Nothing from").length - 1, 2,
    "two causes were collapsed into one sentence");
});

test("the unattributable entries are counted beside the publishers that are named", () => {
  const answer = normalizeSearchResponse(parseGovernedResponse("search", [
    searchOk("lu-legilux", 1),
    { ...searchOk("eu-eurlex", 1), envelope: { status: "ok", publisher: 7 } },
  ]));
  assert.equal(answer.complete, false);
  const sentence = answer.complete === false ? withholdingSentence(answer.withheld) : "";
  assert.ok(sentence.includes("1 further result set was not shown"),
    "an entry with nobody to name was dropped silently");
  assert.ok(!sentence.includes("Nothing from"),
    "an unnamed entry was reported as a named publisher");
});

test("the withholding sentence names nobody rather than naming a hostile list", () => {
  // The same trap `conflictedPublishersSentence` closes, at the second door. A bare string HAS a
  // length, so a guard written against emptiness walks its characters: every character of "eu" is
  // a legal publisher identity, and the sentence would name two publishers that do not exist.
  const empty = { conflicted: [], unreadableScope: [], unattributed: 0 };
  assert.equal(withholdingSentence(empty), "");
  assert.equal(withholdingSentence({ ...empty, conflicted: "eu" as unknown as string[] }), "");
  assert.equal(withholdingSentence({ ...empty, unreadableScope: "eu" as unknown as string[] }), "");
  assert.equal(withholdingSentence({ ...empty, unattributed: -1 }), "");
  assert.equal(withholdingSentence({ ...empty, unattributed: 1.5 }), "");
  // And a real one is not empty, so the assertions above are about the hostile input rather than
  // about a function that returns nothing whatever it is given.
  assert.notEqual(withholdingSentence({ ...empty, conflicted: ["lu-legilux"] }), "");
});

test("the search surface renders the withholding disclosure through this one function", () => {
  // STRUCTURAL, like the second-parser guard in searchPopulationContract.test.ts, because no node
  // test can import a .tsx component. The sentence used to be a private copy inside Search.tsx
  // where nothing could reach it, and it stated a cause the parse never established. Comments are
  // stripped first: a sentence explaining the defect is the opposite of a reintroduction.
  const source = readFileSync(new URL("./Search.tsx", import.meta.url), "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/^[ \t]*\/\/.*$/gm, "");
  assert.ok(source.includes("withholdingSentence(withheld)"),
    "the search surface no longer renders the shared withholding sentence");
  assert.ok(!source.includes("function withholdingSentence"),
    "Search.tsx carries a private copy of the withholding copy again");
  assert.ok(!source.includes("contradict"),
    "the contradiction sentence is back in the search surface");
  // And the strip is fed the one parse rather than the bytes beside it (O1).
  assert.ok(source.includes("envelopeStripRows(parsed)"),
    "the search surface stopped feeding the strip its one parse");
  assert.ok(!source.includes("envelopeStripRows(res)"),
    "the search surface handed raw response bytes to the trust strip again");
});

// ---------------------------------------------------------------------------
// O1: a receipt that expressly proves truncation is never a whole-corpus miss
// ---------------------------------------------------------------------------

test("a publisher receipt counting answers this response never carried forbids absence", () => {
  // THE REVIEWER'S FIRST PROBE. Every per-unit door passes: a valid `ok` search envelope, a
  // coherent population, zero hits, and a `publisher_result_set` whose own three numbers agree
  // with each other exactly as `MarkPublisherSet` derives them. Sibling equality is vacuous on a
  // response of one. Only the number of answers actually in front of this client can see it, and
  // the sentence on the other side of it is the widest claim this product makes.
  const overcounted = {
    ...searchOk("lu-legilux", 0),
    publisher_result_set: { total: 2, returned: 2, maximum: 8, truncated: false },
  };
  assert.equal(classifyEnvelope("search", overcounted).kind, "ran",
    "the fixture stopped classifying ran, so this test would prove nothing");
  assert.equal(parseGovernedResponse("search", [overcounted]).receipts.kind, "irreconcilable");
  const projected = projectSearch([overcounted]);
  assert.notEqual(projected.absence, "no_match",
    "one answer receipted as two claimed the corpus holds nothing");
  assert.equal(projected.absence, "incomplete_response");
  assert.equal(searchEmptyPresentation(projected.absence as "incomplete_response").sentence,
    INCOMPLETE_RESPONSE_SENTENCE);
  // The honest reading of the very same receipt: two answers receipted as two, and the absence
  // is stateable again. Without this the test is satisfied by a verdict that is always
  // irreconcilable, which would prove nothing about the count.
  const receipt = { total: 2, returned: 2, maximum: 8, truncated: false };
  const both = [
    { ...searchOk("lu-legilux", 0), publisher_result_set: receipt },
    { ...searchOk("eu-eurlex", 0), publisher_result_set: receipt },
  ];
  assert.equal(parseGovernedResponse("search", both).receipts.kind, "reconciled",
    "the producer's own two-publisher receipt was refused");
  assert.equal(projectSearch(both).absence, "no_match");
});

test("a truncated publisher set is not a whole-corpus miss", () => {
  // THE REVIEWER'S SECOND PROBE. Eight coherent zero-hit units, every receipt agreeing with every
  // other AND with the eight answers in front of this client, so nothing in the arithmetic is
  // wrong. `truncated` is the producer saying the ninth selected publisher never reached the
  // client at all: `MarkPublisherSet` derives it from `total > MaximumPublisherRows`
  // (McpCore 701-712). A corpus-wide absence over a scope one publisher of which was dropped
  // before the answer was sent is not a claim this response can support.
  const receipt = { total: 9, returned: 8, maximum: 8, truncated: true };
  const eight = Array.from({ length: 8 }, (_, index) =>
    ({ ...searchOk(`pub-${index}`, 0), publisher_result_set: receipt }));
  const parsed = parseGovernedResponse("search", eight);
  assert.equal(parsed.units.length, 8, "the fixture stopped being eight admitted answers");
  assert.equal(parsed.receipts.kind, "reconciled",
    "the arithmetic failed, so this test would be about the count rather than the truncation");
  assert.equal(partitionOf(parsed).invalidCount, 0);
  assert.equal(partitionOf(parsed).moreBeyondPage, true);
  const projected = projectSearch(eight);
  assert.notEqual(projected.absence, "no_match",
    "a publisher dropped from the response was reported as a corpus holding nothing");
  assert.equal(projected.absence, "incomplete_response");
  // Untruncated, the same eight answers are an honest absence and still say so.
  const whole = eight.map((entry) => ({
    ...entry, publisher_result_set: { total: 8, returned: 8, maximum: 8, truncated: false },
  }));
  assert.equal(partitionOf(parseGovernedResponse("search", whole)).moreBeyondPage, false);
  assert.equal(projectSearch(whole).absence, "no_match",
    "a complete publisher set stopped being able to state an absence");
});

test("a truncated row receipt on an empty search page is not a whole-corpus miss", () => {
  // THE REVIEWER'S THIRD PROBE, and the one the partition already knew the answer to.
  // `moreBeyondPage` derived `true` from this receipt and `searchAbsenceState` never read the
  // field, so the same fact that makes a changes page `incomplete_response` left the search page
  // saying the corpus holds nothing.
  const truncated = {
    ...searchOk("lu-legilux", 0),
    response_row_set: { maximum: 20, returned: 0, truncated: true },
  };
  const parsed = parseGovernedResponse("search", [truncated]);
  assert.equal(parsed.receipts.kind, "reconciled",
    "the receipt failed to reconcile, so this test would be about arithmetic instead");
  const partition = partitionOf(parsed);
  assert.equal(partition.invalidCount, 0, "the unit was rejected, so nothing here is about paging");
  assert.equal(partition.moreBeyondPage, true);
  assert.notEqual(searchAbsenceState(partition, 0), "no_match",
    "an empty slice of a page was reported as an empty corpus");
  assert.equal(searchAbsenceState(partition, 0), "incomplete_response");
  assert.equal(projectSearch([truncated]).absence, "incomplete_response");
  // The same receipt saying the page was whole: the absence is stateable again.
  const complete = {
    ...truncated, response_row_set: { maximum: 20, returned: 0, truncated: false },
  };
  assert.equal(partitionOf(parseGovernedResponse("search", [complete])).moreBeyondPage, false);
  assert.equal(projectSearch([complete]).absence, "no_match",
    "an untruncated empty page stopped being an absence");
});

// ---------------------------------------------------------------------------
// O2: the one-unit-per-publisher invariant is counted before admission
// ---------------------------------------------------------------------------

/** The identity fields `Envelope` stamps on every path it builds, as the strip renders them. */
const identified = (publisher: string, hits: number, over: Record<string, unknown> = {}) => {
  const base = searchOk(publisher, hits);
  return {
    ...base,
    envelope: {
      ...base.envelope,
      freshness: {
        built_at: "2026-08-15T09:01:06Z", corpus_commit: "e9c4df09",
        stamp_signature_valid: true,
      },
      artifact: { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" },
      ...over,
    },
  };
};

test("a same-publisher unreadable-scope sibling withholds the claim standing beside it", () => {
  // THE REVIEWER'S FIRST O2 PROBE, and it is an ORDERING defect rather than a missing rule. The
  // invariant was counted over ADMITTED claims, so the rejected sibling that proves the violation
  // had already been discarded by the time anything looked. The page then said both things at
  // once: a Luxembourg hit rendered above a notice reading "Nothing from lu-legilux is shown
  // here."
  const good = identified("lu-legilux", 1);
  const unreadable = { ...identified("lu-legilux", 1), population: "1250" };
  assert.equal(classifyEnvelope("search", good).kind, "ran",
    "the fixture stopped being a valid hit, so this test would prove nothing");
  assert.equal(classifyEnvelope("search", unreadable).kind, "invalid",
    "the sibling stopped being rejected, so there is no discarded evidence to count");
  // ARRIVAL ORDER IS NOT EVIDENCE. Both orders, every seam, one verdict.
  for (const [label, order] of [
    ["rejected second", [good, unreadable]], ["rejected first", [unreadable, good]],
  ] as [string, unknown[]][]) {
    const parsed = parseGovernedResponse("search", order);
    assert.deepEqual(parsed.units, [], `${label}: a claim survived its own publisher's conflict`);
    assert.deepEqual(parsed.conflicted, ["lu-legilux"], `${label}: the invariant saw one unit`);
    // The typed causes stay apart: this publisher really did publish a scope Lex could not read.
    assert.deepEqual(parsed.unreadable, ["lu-legilux"], `${label}: the scope cause was lost`);
    // ROW PROJECTION.
    const projected = projectSearch(order);
    assert.deepEqual(projected.works, [], `${label}: the surviving side was asserted as fact`);
    assert.equal(projected.absence, "incomplete_response", `${label}: an absence was claimed`);
    // DISCLOSURE COPY.
    const normalized = normalizeSearchResponse(parsed);
    assert.equal(normalized.complete, false, `${label}: the response presented itself as whole`);
    assert.deepEqual(normalized.populations, [], `${label}: a denominator survived`);
    const sentence = normalized.complete === false
      ? withholdingSentence(normalized.withheld) : "";
    assert.ok(sentence.includes("lu-legilux"), `${label}: the withheld publisher was not named`);
    assert.ok(sentence.includes("more than one"), `${label}: the conflict cause was not stated`);
    assert.ok(sentence.includes("scope Lex could not read"),
      `${label}: the unreadable-scope cause was absorbed into the conflict`);
    // STRIP PROJECTION.
    const partition = partitionGovernedResponse("search", order);
    assert.deepEqual(partition.stripRows.map((row) => row.publisher), ["lu-legilux"],
      `${label}: the mounted publisher was dropped instead of disclosed`);
    assert.equal(partition.stripRows[0].builtAt, undefined,
      `${label}: a build date survived a publisher this response established nothing for`);
    assert.equal(partition.stripRows[0].signatureValid, undefined,
      `${label}: a signature verdict survived a withheld publisher`);
  }
  // A DIFFERENT publisher is not a conflict at all, so the guard is about the invariant rather
  // than about rejecting anything that arrives beside a rejected unit.
  const distinct = projectSearch([good, { ...identified("eu-eurlex", 1), population: "1250" }]);
  assert.deepEqual(distinct.works, [{ work: "lu-legilux:w0" }],
    "a rejected unit from another publisher withheld a claim it says nothing about");
});

test("a same-publisher malformed sibling withdraws the good-signature strip", () => {
  // THE REVIEWER'S SECOND O2 PROBE. The rejected sibling here fails on its ROWS, not its scope,
  // and it carries an index identity of its own. Two identities for one index establish neither,
  // so the strongest claim on the page, a build date and a valid-signature badge, may not be
  // taken from whichever of them this table happened to be able to read.
  const good = identified("lu-legilux", 1);
  const malformed = {
    ...identified("lu-legilux", 1, {
      freshness: {
        built_at: "2019-01-01T00:00:00Z", corpus_commit: "deadbeef",
        stamp_signature_valid: false,
      },
      artifact: { code_commit: "zzz999", manifest_set_id: "m-9", content_digest: "d-9" },
    }),
    hits: [null],
  };
  assert.equal(classifyEnvelope("search", malformed).kind, "invalid",
    "the sibling stopped being rejected, so this test would prove nothing");
  for (const [label, order] of [
    ["malformed second", [good, malformed]], ["malformed first", [malformed, good]],
  ] as [string, unknown[]][]) {
    const parsed = parseGovernedResponse("search", order);
    assert.deepEqual(parsed.conflicted, ["lu-legilux"], `${label}: the invariant saw one unit`);
    // The cause is a malformed unit, NOT an unreadable scope, and the two sentences stay apart.
    assert.deepEqual(parsed.unreadable, [],
      `${label}: a malformed row list was reported as a scope this client could not read`);
    // ROW PROJECTION.
    const projected = projectSearch(order);
    assert.deepEqual(projected.works, [], `${label}: the readable side was asserted as fact`);
    assert.equal(projected.absence, "incomplete_response", `${label}: an absence was claimed`);
    // DISCLOSURE COPY.
    const normalized = normalizeSearchResponse(parsed);
    assert.equal(normalized.complete, false, `${label}: the response presented itself as whole`);
    const sentence = normalized.complete === false
      ? withholdingSentence(normalized.withheld) : "";
    assert.ok(sentence.includes("lu-legilux"), `${label}: the withheld publisher was not named`);
    assert.ok(sentence.includes("more than one"), `${label}: the conflict cause was not stated`);
    assert.ok(!sentence.includes("scope Lex could not read"),
      `${label}: a malformed sibling was reported as an unreadable scope`);
    // STRIP PROJECTION: the confident row is gone and the publisher is still disclosed.
    const rows = partitionGovernedResponse("search", order).stripRows;
    assert.deepEqual(rows.map((row) => row.publisher), ["lu-legilux"],
      `${label}: the mounted publisher was dropped instead of disclosed`);
    assert.equal(rows[0].signatureValid, undefined,
      `${label}: a valid-signature badge survived two identities for one index`);
    assert.equal(rows[0].builtAt, undefined,
      `${label}: a build date survived two identities for one index`);
    assert.equal(rows[0].corpusCommit, undefined,
      `${label}: a corpus commit survived two identities for one index`);
  }
  // Alone, the good unit still states its identity, so the assertions above are about the
  // conflict rather than about a strip that has stopped disclosing anything.
  const alone = partitionGovernedResponse("search", [good]).stripRows;
  assert.equal(alone[0].signatureValid, true, "the strip stopped disclosing a good signature");
  assert.equal(alone[0].builtAt, "2026-08-15T09:01:06Z");
  // AND AN UNATTRIBUTABLE REJECTION IS STILL UNATTRIBUTABLE. An entry with no readable envelope
  // names no publisher, so it triggers no invariant and withholds nobody's claim; it stays in the
  // unusable count exactly where it was.
  const anonymous = projectSearch([good, { envelope: "not an object" }]);
  assert.deepEqual(anonymous.works, [{ work: "lu-legilux:w0" }],
    "a rejection that names nobody was attributed to a publisher anyway");
  assert.deepEqual(parseGovernedResponse("search", [good, { envelope: "x" }]).conflicted, [],
    "an unattributable rejection was counted against a publisher's unit total");
});

// ---------------------------------------------------------------------------
// O3: a changes refusal answers for its own mandatory receipts
// ---------------------------------------------------------------------------

test("a changes refusal must carry the explicit addend its global receipt sums", () => {
  // `global_response_row_set.total` is `totalAcrossPublishers`, the sum of every stamped unit's
  // `works_changed`, and the producer stamps that field on the refusal itself
  // (`refusal["works_changed"] = 0`, McpCore 1779). The reconciliation read it with `?? 0`, so a
  // refusal that carried no addend at all was silently counted as one that carried zero: the
  // arithmetic balanced against a figure the response never stated, and the reader was told
  // "No selected publisher ran this query."
  const global = { offset: 0, maximum: 20, returned: 0, total: 0, truncated: false };
  const silent = {
    ...refused("eu-eurlex", ["domain"], "changes_in_period"), global_response_row_set: global,
  };
  assert.equal(classifyEnvelope("changes_in_period", silent).kind, "refused",
    "the fixture stopped being a coherent refusal, so this test would prove nothing");
  assert.equal(parseGovernedResponse("changes_in_period", [silent]).receipts.kind,
    "irreconcilable");
  const decision = projectGovernedEmptiness("changes_in_period", [silent], 0);
  assert.notEqual(decision.empty, "all_refused",
    "a receipt was reconciled against a zero no unit of this response ever stated");
  assert.equal(decision.empty, "incomplete_response");
  assert.equal(decision.partial, true);
  // The producer's own refusal states the zero, and still reaches the coverage sentence.
  const stated = { ...silent, works_changed: 0 };
  assert.equal(parseGovernedResponse("changes_in_period", [stated]).receipts.kind, "reconciled",
    "the producer's own refusal shape was refused, so this test proves nothing");
  assert.equal(projectGovernedEmptiness("changes_in_period", [stated], 0).empty, "all_refused");
  // A ran unit cannot reach that guard at all: `requiredCounts` refuses it one door earlier. So
  // the rule is stated over every sibling and is REACHED only by the non-executing ones, and
  // saying so here is what keeps the guard honest about which units it protects.
  const worksChangedDropped = (entry: Record<string, unknown>) => {
    const { works_changed: _dropped, ...rest } = entry;
    return { ...rest, global_response_row_set: global };
  };
  assert.equal(classifyEnvelope("changes_in_period",
    worksChangedDropped(changesOk("lu-legilux", 0))).kind, "invalid",
    "a ran unit is refused for the missing count before any receipt is reconciled");

  // AND AN ADDEND OF ITS OWN IS REFUSED, not only a missing one. This is the second half of the
  // same rule and it needs its own probe, because the sum cannot see it: a refusal claiming five
  // changed works beside a sibling that found none still totals the five the receipt prints, so
  // the arithmetic agrees about a figure no unit of this response ever sent.
  // Five changed works either way, so the total the receipt prints is supported by the sum in
  // both readings and only the second clause can tell them apart. The ran sibling takes the
  // status its own count coheres with: `works == 0 ? NoChangesInPeriod : Ok`, exactly as the
  // producer chooses it, and a paged-out publisher legitimately reports its full period total
  // beside no rows at all.
  const cancelled = (claimedByTheRefusal: number) => {
    const mine = 5 - claimedByTheRefusal;
    const carried = { offset: 0, maximum: 20, returned: 0, total: 5, truncated: true };
    return [
      { ...refused("eu-eurlex", ["domain"], "changes_in_period"),
        works_changed: claimedByTheRefusal, global_response_row_set: carried },
      { envelope: {
          status: mine === 0 ? "no_changes_in_period" : "ok", publisher: "lu-legilux",
        },
        changes: [], works_changed: mine, new_versions: 0,
        population: changesPopulation(), global_response_row_set: carried },
    ];
  };
  assert.equal(parseGovernedResponse("changes_in_period", cancelled(0)).receipts.kind,
    "reconciled",
    "the honest split was refused, so this probe would prove nothing about the forged one");
  assert.equal(parseGovernedResponse("changes_in_period", cancelled(5)).receipts.kind,
    "irreconcilable",
    "a publisher that refused to look claimed five changed works and the total absorbed it");
  assert.equal(projectGovernedEmptiness("changes_in_period", cancelled(5), 0).partial, true,
    "a report whose addends nothing supports presented itself as coherent");
});

test("a changes refusal's row receipt must be the zero-row shape its producer mints", () => {
  // `response_row_set` is PER UNIT on this tool alone, minted from that unit's own `shown` and
  // `works_changed` (McpCore 1865-1875), so a refusal's copy is reconciled against no sibling and
  // `pagingOf` never sees it: that validator is ran-only, correctly, because the rows a receipt
  // must cohere with exist only on the executed path. A refusal receipting seven returned change
  // rows therefore passed every door and reached the reader as a coverage statement.
  const refusal = {
    ...refused("eu-eurlex", ["domain"], "changes_in_period"), works_changed: 0,
  };
  const loud = {
    ...refusal, response_row_set: { maximum: 20, returned: 7, truncated: false },
  };
  assert.equal(classifyEnvelope("changes_in_period", loud).kind, "refused",
    "the fixture stopped being a coherent refusal, so this test would prove nothing");
  assert.equal(parseGovernedResponse("changes_in_period", [loud]).receipts.kind,
    "irreconcilable");
  const decision = projectGovernedEmptiness("changes_in_period", [loud], 0);
  assert.notEqual(decision.empty, "all_refused",
    "a publisher that never looked receipted seven returned change rows and was believed");
  assert.equal(decision.empty, "incomplete_response");
  // `truncated` is minted as `works_changed > shown`, which on a refusal is `0 > 0`.
  const truncating = {
    ...refusal, response_row_set: { maximum: 20, returned: 0, truncated: true },
  };
  assert.equal(parseGovernedResponse("changes_in_period", [truncating]).receipts.kind,
    "irreconcilable");
  // The producer's own refusal receipt, which still reaches the coverage sentence.
  const minted = {
    ...refusal, response_row_set: { maximum: 20, returned: 0, truncated: false },
  };
  assert.equal(parseGovernedResponse("changes_in_period", [minted]).receipts.kind, "reconciled",
    "the producer's own refusal receipt was refused, so this test proves nothing");
  assert.equal(projectGovernedEmptiness("changes_in_period", [minted], 0).empty, "all_refused");
  // AND NOT ON THE OTHER TWO TOOLS. `MarkResponseRows` stamps ONE response-wide object on every
  // item of search and in_force_on, refusals included, so their refusals legitimately receipt the
  // rows their siblings shipped. Applying the zero-row rule there would refuse the producer.
  const searchPair = [
    { ...refused("eu-eurlex", ["domain"]),
      response_row_set: { maximum: 20, returned: 1, truncated: false } },
    { ...searchOk("lu-legilux", 1),
      response_row_set: { maximum: 20, returned: 1, truncated: false } },
  ];
  assert.equal(parseGovernedResponse("search", searchPair).receipts.kind, "reconciled",
    "a search refusal was made to answer for the rows its sibling shipped");
});

test("a changes refusal reporting changed works of its own is refused, not believed", () => {
  // WIDER THAN THE REVIEW'S OWN CONDITION, and deliberately so, on the mint site rather than on
  // judgement. `McpCore` 1779 writes `refusal["works_changed"] = 0` unconditionally, immediately
  // after the refusal object is built, so a changes refusal reporting a changed work is a shape
  // the producer cannot emit whatever else the response carries. Binding the rule only to
  // responses that also stamped a `global_response_row_set` left the forgery believed on every
  // response that did not happen to carry one, and the surface then answered "No selected
  // publisher ran this query." beside a unit saying a work had changed.
  const forged = {
    ...refused("eu-eurlex", ["domain"], "changes_in_period"),
    works_changed: 1, new_versions: 1,
  };
  assert.equal(classifyEnvelope("changes_in_period", forged).kind, "refused",
    "the fixture stopped being a coherent refusal, so this test would prove nothing");
  const parsed = parseGovernedResponse("changes_in_period", [forged]);
  // NO RESPONSE-WIDE RECEIPT ANYWHERE IN IT, which is the whole of the widening: the earlier rule
  // could only fire inside the reconciliation of a receipt this response never stamped.
  assert.equal(parsed.units[0].receipts.globalRowSet, undefined,
    "the fixture grew the receipt whose absence this test is about");
  assert.equal(parsed.units[0].receipts.rowSet, undefined,
    "the fixture grew a row receipt, so another rule could be doing the refusing");
  assert.equal(parsed.receipts.kind, "irreconcilable");
  const decision = projectGovernedEmptiness("changes_in_period", [forged], 0);
  assert.notEqual(decision.empty, "all_refused",
    "a publisher that refused to look reported a changed work and was believed");
  assert.equal(decision.empty, "incomplete_response");
  // Row authority is untouched by the count: the refusal's rows were never renderable and are
  // still not, which is the property the row-authority test above pins on the corrected fixture.
  assert.deepEqual(decision.partition.ran, [],
    "a refusal contributed rows once its count was refused");
  // The producer's own zero still reaches the coverage sentence, so this is a rule about the
  // forgery rather than a client that has stopped believing refusals.
  const minted = { ...forged, works_changed: 0, new_versions: 0 };
  assert.equal(parseGovernedResponse("changes_in_period", [minted]).receipts.kind, "reconciled",
    "the producer's own refusal shape was refused, so this test proves nothing");
  assert.equal(projectGovernedEmptiness("changes_in_period", [minted], 0).empty, "all_refused");
  // AND NOT ON A TOOL THAT MINTS NO SUCH COUNT. in_force_on publishes no per-unit total for a
  // response-wide receipt to sum, so the same field on its refusal is a stray the table never
  // reads, and applying a changes rule to it would refuse a response for a shape it never had.
  assert.equal(projectGovernedEmptiness("in_force_on",
    [{ ...refused("lu-legilux", ["hierarchy"], "in_force_on"), works_changed: 1 }], 0).empty,
    "all_refused", "a rule minted for changes_in_period was applied to in_force_on");
});
