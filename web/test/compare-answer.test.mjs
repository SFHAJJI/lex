// The V3 comparison reader, held to the answers the real handler sent.
//
// The answers come from `schemas/v3-platform/answer-samples.json` (operation `diff`), captured by
// driving the real handler: the fixture's one state against itself, and a second fixture's two
// states a year apart with one article reworded (`art_15`) and one id renamed (`art_16` to
// `art_16-new`). Each rule the answer states about itself is then broken once.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { ARTICLE_STATUSES, readDiff } from "../scripts/compare-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswers() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "diff");
  assert.equal(rows.length, 2, "the census holds the two comparisons this reader is written against");
  for (const row of rows) assert.equal(row.object_type, "diff");
  const [same, two] = rows.map((row) => withDigests(row.answer));
  return { same, two };
}

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

const rowOf = (answer, id) => answer.comparisons[0].articles.find((row) => row.publisher_id === id);

test("the same state on both dates reads as such: no articles, no counts", async () => {
  const { same } = await capturedAnswers();
  const view = readDiff(same);
  const [comparison] = view.comparisons;
  assert.equal(comparison.sameState, true);
  assert.equal(comparison.articles, null);
  assert.equal(comparison.counts, null);
  assert.equal(comparison.from.stateSha256, comparison.to.stateSha256);
});

test("two states read article by article: one changed, one removed, one added, the rest unchanged", async () => {
  const { two } = await capturedAnswers();
  const view = readDiff(two);
  const [comparison] = view.comparisons;
  assert.equal(comparison.sameState, false);
  assert.deepEqual(comparison.counts, { unchanged: 47, changed: 1, added: 1, removed: 1 });
  const byId = Object.fromEntries(comparison.articles.map((row) => [row.publisherId, row.status]));
  assert.equal(byId.art_15, "changed");
  assert.equal(byId.art_16, "removed");
  assert.equal(byId["art_16-new"], "added");
  assert.ok(comparison.note.includes("nothing about legal effect is asserted"));
  assert.deepEqual(ARTICLE_STATUSES, ["unchanged", "changed", "added", "removed"]);
});

test("each rule the comparison states about itself is refused when broken, with that rule's reason", async () => {
  const { same, two } = await capturedAnswers();
  const cases = [
    ["another publisher's comparison", two, (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) comparison only/],
    ["a status its sides do not make", two, (a) => { rowOf(a, "art_15").status = "unchanged"; }, /says "unchanged", and its sides make it changed/],
    ["a renamed id said unchanged", two, (a) => { rowOf(a, "art_16").status = "unchanged"; }, /its sides make it removed/],
    ["counts that are not the rows'", two, (a) => { a.comparisons[0].counts.unchanged = 46; }, /counts 46 unchanged, and 47 of its rows are/],
    ["an article of the state missing", two, (a) => { a.comparisons[0].articles.splice(0, 1); a.comparisons[0].counts.unchanged -= 1; }, /do not cover the articles/],
    ["rows out of the ids' order", two, (a) => { a.comparisons[0].articles.reverse(); }, /ordinal order, each once/],
    ["a row on neither side", two, (a) => { const row = rowOf(a, "art_15"); row.from = []; row.to = []; }, /is on neither side/],
    ["same_state against the sides", two, (a) => { a.comparisons[0].same_state = true; }, /its sides are two states/],
    ["the same state with articles", same, (a) => { a.comparisons[0].counts = { unchanged: 0, changed: 0, added: 0, removed: 0 }; }, /compares no article and counts none/],
    ["a side that applies after its date", two, (a) => { a.requested_date_to = "2025-03-06"; }, /after its date 2025-03-06/],
    ["a side followed before its date", two, (a) => { a.comparisons[0].from.next_applicability_date = "2024-02-01"; }, /does not apply on its date/],
    ["a side in another language", two, (a) => { a.comparisons[0].to.language = "deu"; }, /is in deu, in the comparison of fra/],
    ["a permalink to another digest", two, (a) => { a.comparisons[0].to.permalink = a.comparisons[0].to.permalink.slice(0, -1) + "0"; }, /does not pin its coordinate and digest/],
    ["a validity count against the flags", two, (a) => { a.comparisons[0].from.validity_conflict_count -= 1; }, /validity_conflict_count is/],
    ["a language compared twice", two, (a) => { a.languages_not_compared = [{ language: "fra", bound: "from", reason: "no state at or before the date" }]; }, /compared or listed as not compared twice/],
    ["a bound that is not one", two, (a) => { a.available_languages = ["deu", "fra"]; a.requested_language = null; a.languages_not_compared = [{ language: "deu", bound: "both", reason: "x" }]; }, /a bound is from or to/],
    ["a language not compared, accounted for", two, (a) => { a.available_languages = ["deu", "fra"]; a.requested_language = null; a.languages_not_compared = [{ language: "deu", bound: "from", reason: "no state at or before the date" }]; }, null],
    ["a language asked that is not held", two, (a) => { a.requested_language = "eng"; }, /not one the work is held in/],
    ["nothing compared", two, (a) => { a.comparisons = []; }, /refused no_version_for_date, not answered/],
    ["a held language compared nowhere", two, (a) => { a.requested_language = null; a.available_languages = ["deu", "fra"]; }, /accounts for fra, and its scope is deu, fra/],
    ["a side's dated article not among its identities", two, (a) => { a.comparisons[0].from.articles[0].article_identity_sha256 = "f".repeat(64); }, /dated articles are not its article_identities/],
    ["a missing member", two, (a) => { delete rowOf(a, "art_15").to[0].wording_sha256; }, /does not carry wording_sha256/],
    ["a digest the census left unfilled", two, (a) => { a.index_sha256 = PLACEHOLDER; }, /index_sha256 is not a SHA-256 digest/],
  ];
  for (const [what, base, change, reason] of cases) {
    if (reason === null) {
      assert.doesNotThrow(() => readDiff(mutate(base, change)), what);
    } else {
      assert.throws(() => readDiff(mutate(base, change)), reason, what);
    }
  }
});

test("the pre-V3 compare shape is not read as a V3 comparison", () => {
  assert.throws(() => readDiff({ left: { lex_id: "x" }, right: { lex_id: "y" }, hunks: [] }), /does not carry publisher/);
});
