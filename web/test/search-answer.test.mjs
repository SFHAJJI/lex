// The V3 search reader, held to the answers the real handler sent.
//
// The answers come from `schemas/v3-platform/answer-samples.json` (operation `search`), captured
// by driving the real handler on the fixture mount, so the reader is written against what the
// platform sends and a change to that shape fails here before any page renders it. Each rule the
// answer states about itself is then broken once, and the reader must refuse the broken answer
// with that rule's reason.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { LANE_MATCH_REASON, SEARCH_LANES, readSearch } from "../scripts/search-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswers() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "search");
  assert.equal(rows.length, 3, "the census holds the three search answers this reader is written against");
  for (const row of rows) assert.equal(row.object_type, "quote");
  const [both, paged, none] = rows.map((row) => withDigests(row.answer));
  return { both, paged, none };
}

/** Fills the fields the census normalises, and only those, each with its own digest. */
function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") {
    return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  }
  if (node === PLACEHOLDER) {
    counter.n += 1;
    return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64);
  }
  return node;
}

function mutate(answer, change) {
  const copy = structuredClone(answer);
  change(copy);
  return copy;
}

test("the three captured answers read: both lanes, a truncated page with its cursor, and no hit", async () => {
  const { both, paged, none } = await capturedAnswers();

  const view = readSearch(both);
  assert.equal(view.query, "assemblée générale");
  assert.deepEqual(view.terms, ["assemblée", "générale"]);
  assert.deepEqual(view.hits.map((hit) => hit.lane), ["strict", "strict", "strict", "strict", "relaxed"]);
  assert.deepEqual(view.hits.map((hit) => hit.matchReason), [...Array(4).fill("exact_phrase"), "all_terms"]);
  assert.equal(view.population.strictHits + view.population.relaxedHits, view.hits.length);
  assert.equal(view.truncated, false);
  assert.equal(view.continueAfter, null);
  for (const hit of view.hits) {
    assert.equal(hit.permalink, `/lu-legilux/${hit.workKey}/${hit.applicabilityDate}--${hit.stateSha256}`);
  }

  const page = readSearch(paged);
  assert.equal(page.hits.length, 1);
  assert.equal(page.truncated, true);
  assert.equal(page.continueAfter, `strict.${page.hits[0].stateSha256}.${page.hits[0].articleIdentitySha256}`);
  assert.deepEqual(page.hits[0], view.hits[0], "the first page of one is the first hit of the whole answer");

  const empty = readSearch(none);
  assert.equal(empty.hits.length, 0);
  assert.equal(empty.population.strictHits + empty.population.relaxedHits, 0);
  assert.equal(empty.searchableTextHeld, true, "no hit is an answer over held text, not an absence of text");
});

test("each rule the answer states about itself is refused when broken, with that rule's reason", async () => {
  const { both, paged } = await capturedAnswers();
  const cases = [
    ["a lane the index does not hold", both, (a) => { a.hits[0].lane = "bm25"; }, /the index holds strict and relaxed/],
    ["a reason that is not its lane's", both, (a) => { a.hits[0].match_reasons = ["all_terms"]; }, /matched by exact_phrase and nothing else/],
    ["two reasons", both, (a) => { a.hits[4].match_reasons = ["all_terms", "exact_phrase"]; }, /matched by all_terms and nothing else/],
    ["relaxed before strict", both, (a) => { a.hits.reverse(); }, /relaxed never outranks strict/],
    ["a permalink to another state", both, (a) => { a.hits[1].resolve.identifier = a.hits[1].resolve.identifier.slice(0, -1) + "0"; }, /not to the state it names/],
    ["one article twice", both, (a) => { a.hits[1] = structuredClone(a.hits[0]); }, /twice/],
    ["more hits than the limit", both, (a) => { a.limit = 2; }, /against a limit of 2/],
    ["a truncated page without its cursor", paged, (a) => { a.continue_after = null; }, /exactly when it is truncated/],
    ["a cursor on a whole page", both, (a) => { a.continue_after = "strict.x.y"; }, /exactly when it is truncated/],
    ["a cursor that is not the last hit", paged, (a) => { a.continue_after = a.continue_after.replace("strict.", "relaxed."); }, /not the last hit/],
    ["a population that does not add up", both, (a) => { a.population.strict_hits = 3; }, /counts 4 hits and the untruncated page holds 5/],
    ["a truncated page from a population no larger", paged, (a) => { a.population.strict_hits = 1; a.population.relaxed_hits = 0; }, /more than that/],
    ["a missing member", both, (a) => { delete a.hits[2].publisher_id; }, /hit 3 does not carry publisher_id/],
    ["a digest the census left unfilled", both, (a) => { a.corpus_sha256 = PLACEHOLDER; }, /corpus_sha256 is not a SHA-256 digest/],
    ["a date that is not one", both, (a) => { a.hits[0].applicability_date = "2024-02-30"; }, /not a calendar date/],
    ["no terms", both, (a) => { a.terms = []; }, /at least one term/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readSearch(mutate(base, change)), reason, what);
  }
});

test("the pre-V3 search shape is not read as a V3 answer", () => {
  const retired = {
    query: "loyer",
    hits: [{ lex_id: "lu-legilux:x", provision_num: "Art. 1", match_reasons: ["keyword"] }],
    row_set: { returned: 1 },
  };
  assert.throws(() => readSearch(retired), /does not carry requested_query/);
});

test("the lanes and their reasons are the platform's closed pairs", () => {
  assert.deepEqual(SEARCH_LANES, ["strict", "relaxed"]);
  assert.deepEqual(LANE_MATCH_REASON, { strict: "exact_phrase", relaxed: "all_terms" });
});
