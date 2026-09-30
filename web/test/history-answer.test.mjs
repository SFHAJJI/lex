// The V3 provision history reader, held to the answer the real handler sent.
//
// The answer comes from `schemas/v3-platform/answer-samples.json` (operation `article_history`),
// captured by driving the real handler on the fixture mount: one state carrying `art_15`. A longer
// lineage (a later state that does not carry the anchor, then one with a changed wording, and a
// second language) is built from it the way `V3CorpusMount.ArticleHistory` builds one, and each rule
// the answer states about itself is then broken once.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { readArticleHistory } from "../scripts/history-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswer() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "article_history");
  assert.equal(rows.length, 1, "the census holds the provision history this reader is written against");
  assert.equal(rows[0].object_type, "provision_history");
  return withDigests(rows[0].answer);
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

function stateRef(answer, language, date, digit) {
  const sha = digit.repeat(64);
  return { language, applicability_date: date, state_sha256: sha, permalink: `/lu-legilux/${answer.work_key}/${date}--${sha}` };
}

function row(answer, language, date, digit, wording, next) {
  const base = structuredClone(answer.states[0]);
  const ref = stateRef(answer, language, date, digit);
  return {
    ...base,
    ...ref,
    stable_coordinate: `/lu-legilux/${answer.work_key}/${date}`,
    next_applicability_date: next,
    articles: base.articles.map((article) => ({
      ...article,
      article_identity_sha256: `${digit}${article.article_identity_sha256.slice(1)}`,
      validity_conflict: article.article_valid_from !== null && article.article_valid_from !== date,
      wording_sha256: wording,
    })),
    wording_changed: false,
  };
}

/**
 * The captured row, then a French state that does not carry the anchor, then a French row whose
 * wording changed, and a German row: as the producer builds the lineage, rows and absent states each
 * in scope order, the first row's next date the absent state's.
 */
function lineage(base) {
  return mutate(base, (answer) => {
    const first = answer.states[0];
    const wording = first.articles[0].wording_sha256;
    first.next_applicability_date = "2025-01-01";
    const german = row(answer, "deu", "2024-06-01", "d", "d".repeat(64), null);
    const later = row(answer, "fra", "2026-01-01", "e", "e".repeat(64), null);
    later.wording_changed = true;
    answer.states = [first, german, later];
    answer.absent_in_states = [stateRef(answer, "fra", "2025-01-01", "c")];
    answer.available_languages = ["deu", "fra"];
    answer.requested_language = null;
    answer.wording_runs = [{ language: "deu", count: 1 }, { language: "fra", count: 2 }];
    answer.distinct_wordings = [{ language: "deu", count: 1 }, { language: "fra", count: 2 }];
    assert.notEqual(wording, later.articles[0].wording_sha256);
  });
}

test("the captured history reads: one row carrying the anchor, no absence, one run of one wording", async () => {
  const answer = await capturedAnswer();
  const view = readArticleHistory(answer);
  assert.equal(view.anchor, "art_15");
  assert.equal(view.rows.length, 1);
  assert.deepEqual(view.absent, []);
  assert.equal(view.rows[0].wordingChanged, false);
  assert.deepEqual(view.wordingRuns, { fra: 1 });
  assert.deepEqual(view.distinctWordings, { fra: 1 });
  assert.equal(view.historyBegins, view.rows[0].applicabilityDate);
});

test("a longer lineage reads: an absent state between two rows, a changed wording, and a second language", async () => {
  const view = readArticleHistory(lineage(await capturedAnswer()));
  assert.deepEqual(view.rows.map((row) => [row.language, row.applicabilityDate, row.nextApplicabilityDate, row.wordingChanged]), [
    ["fra", "2024-02-01", "2025-01-01", false],
    ["deu", "2024-06-01", null, false],
    ["fra", "2026-01-01", null, true],
  ], "a row's next state is the next in scope, carrying the anchor or not");
  assert.deepEqual(view.absent.map((state) => state.applicabilityDate), ["2025-01-01"]);
  assert.deepEqual(view.wordingRuns, { deu: 1, fra: 2 });
});

test("each rule the history states about itself is refused when broken, with that rule's reason", async () => {
  const answer = await capturedAnswer();
  const long = lineage(answer);
  const cases = [
    ["another publisher's history", answer, (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) history only/],
    ["an article of another anchor", answer, (a) => { a.states[0].articles[0].publisher_id = "art_16"; }, /carries the anchor asked for, art_15/],
    ["a validity flag against the dates", answer, (a) => { a.states[0].articles[0].validity_conflict = !a.states[0].articles[0].validity_conflict; }, /says validity_conflict is/],
    ["a change that is not one", long, (a) => { a.states[2].wording_changed = false; }, /says wording_changed is false, and its wording differs/],
    ["a first row said to change", answer, (a) => { a.states[0].wording_changed = true; }, /says wording_changed is true, and its wording is the previous/],
    ["runs that are not the rows'", long, (a) => { a.wording_runs[1].count = 1; }, /wording_runs are not/],
    ["distinct wordings that are not the rows'", long, (a) => { a.distinct_wordings[1].count = 3; }, /distinct_wordings are not/],
    ["counts out of the languages' order", long, (a) => { a.wording_runs.reverse(); }, /out of the languages' order/],
    ["a history that begins elsewhere", long, (a) => { a.history_begins = "2025-01-01"; }, /first row is from 2024-02-01/],
    ["a next date that skips the absent state", long, (a) => { a.states[0].next_applicability_date = "2026-01-01"; }, /next later state in scope is from 2025-01-01/],
    ["a state both carried and absent", long, (a) => { a.absent_in_states.push({ ...a.states[2], stable_coordinate: undefined }); }, /listed twice/],
    ["rows out of date order", long, (a) => { a.states.reverse(); }, /date order/],
    ["a permalink to another digest", answer, (a) => { a.states[0].permalink = a.states[0].permalink.slice(0, -1) + "0"; }, /does not pin its work, date and digest/],
    ["a coordinate of another date", answer, (a) => { a.states[0].stable_coordinate = `/lu-legilux/${a.work_key}/2020-01-01`; }, /does not name its work and date/],
    ["a state in another language than asked", long, (a) => { a.requested_language = "fra"; }, /is in deu, and the history was asked in fra/],
    ["a language asked that is not held", answer, (a) => { a.requested_language = "eng"; }, /not one the work is held in/],
    ["a row carrying no article", answer, (a) => { a.states[0].articles = []; }, /carries no article/],
    ["no row at all", answer, (a) => { a.states = []; }, /refused anchor_not_in_version, not answered/],
    ["a missing member", answer, (a) => { delete a.states[0].articles[0].wording_sha256; }, /does not carry wording_sha256/],
    ["a digest the census left unfilled", answer, (a) => { a.corpus_sha256 = PLACEHOLDER; }, /corpus_sha256 is not a SHA-256 digest/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readArticleHistory(mutate(base, change)), reason, what);
  }
});

test("the pre-V3 history shape is not read as a V3 answer", () => {
  assert.throws(() => readArticleHistory({ provision: { lex_id: "x" }, versions: [] }), /does not carry publisher/);
});
