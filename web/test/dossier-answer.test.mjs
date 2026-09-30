// The V3 dossier reader, held to the answer the real handler sent.
//
// The answer comes from `schemas/v3-platform/answer-samples.json` (operation `dossier`), captured
// by driving the real handler on the fixture mount. The fixture holds one work with one state and no
// titles, so a work with several states in two languages, and one with titles, are built from that
// answer the way the producer builds them (`V3CorpusMount.Dossier`), and each rule the answer states
// about itself is then broken once.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { readDossier } from "../scripts/dossier-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswer() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "dossier");
  assert.equal(rows.length, 1, "the census holds the dossier answer this reader is written against");
  assert.equal(rows[0].object_type, "work_record");
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

/** A state of the same work on another date and language, built as the producer builds one. */
function stateOn(base, date, language, digit) {
  const sha = digit.repeat(64);
  const coordinate = `/lu-legilux/${base.work_key}/${date}`;
  return {
    ...structuredClone(base.states[0]),
    language,
    applicability_date: date,
    next_applicability_date: null,
    state_sha256: sha,
    expression_iri: `${base.states[0].expression_iri.replace(/\/fr$/, "")}/${language.slice(0, 2)}-${date}`,
    stable_coordinate: coordinate,
    permalink: `${coordinate}--${sha}`,
  };
}

/** The sample with a later French state and a German state between them, the next dates and totals set. */
function severalStates(base) {
  return mutate(base, (answer) => {
    const first = answer.states[0];
    const german = stateOn(answer, "2025-01-01", "deu", "d");
    const later = stateOn(answer, "2026-01-01", "fra", "e");
    first.next_applicability_date = "2026-01-01";
    answer.states = [first, german, later];
    answer.available_languages = ["deu", "fra"];
    answer.requested_language = null;
    answer.state_count = 3;
    answer.latest_applicability_date = "2026-01-01";
  });
}

test("the captured answer reads: the work, its one state, and what the dossier does not hold", async () => {
  const answer = await capturedAnswer();
  const view = readDossier(answer);
  assert.equal(view.workKey, "loi-1991-08-10-n3");
  assert.equal(view.language, "fra");
  assert.equal(view.states.length, 1);
  const [state] = view.states;
  assert.equal(state.permalink, `/lu-legilux/${view.workKey}/${state.applicabilityDate}--${state.stateSha256}`);
  assert.equal(state.nextApplicabilityDate, null, "the only state has no next date");
  assert.equal(state.articleCount, 49);
  assert.equal(state.articlesNotAdmitted, 5);
  assert.deepEqual(view.titles, [], "the fixture holds no titles, and the view says none, not a guess");
  assert.equal(view.notHeld.length, answer.not_held.length);
  assert.ok(view.notHeld.every((row) => row.reason.length > 0), "each item not held carries its reason");
});

test("a work with several states in two languages and with titles reads, as the producer builds it", async () => {
  const answer = await capturedAnswer();
  const view = readDossier(severalStates(answer));
  assert.deepEqual(view.states.map((state) => [state.language, state.applicabilityDate, state.nextApplicabilityDate]), [
    ["fra", "2024-02-01", "2026-01-01"],
    ["deu", "2025-01-01", null],
    ["fra", "2026-01-01", null],
  ], "a state's next date is the next later date in its own language");

  const titled = mutate(answer, (a) => {
    a.titles = [{
      language: "fra",
      expression_iri: a.states[0].expression_iri,
      titles: [{ title: "Loi du 10 août 1991", evidence_sha256: "a".repeat(64) }],
      short_titles: [],
    }];
  });
  assert.equal(readDossier(titled).titles[0].titles[0].title, "Loi du 10 août 1991");
});

test("each rule the answer states about itself is refused when broken, with that rule's reason", async () => {
  const answer = await capturedAnswer();
  const several = severalStates(answer);
  const cases = [
    ["another publisher's answer", answer, (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) dossier only/],
    ["a state of another work", answer, (a) => { a.states[0].publisher_work_iri = "http://elsewhere/eli/x"; }, /not to the work/],
    ["a permalink to another digest", answer, (a) => { a.states[0].permalink = a.states[0].permalink.slice(0, -1) + "0"; }, /does not pin its coordinate and digest/],
    ["a coordinate of another date", answer, (a) => { a.states[0].stable_coordinate = `/lu-legilux/${a.work_key}/2020-01-01`; }, /does not name its work and date/],
    ["a next date that is not the next state's", several, (a) => { a.states[0].next_applicability_date = "2025-01-01"; }, /next later state in fra is 2026-01-01/],
    ["a next date on the last state", answer, (a) => { a.states[0].next_applicability_date = "2030-01-01"; }, /next later state in fra is null/],
    ["states out of date order", several, (a) => { a.states.reverse(); }, /date order/],
    ["one state twice", answer, (a) => { a.states.push(structuredClone(a.states[0])); a.state_count = 2; }, /is listed twice/],
    ["a count that is not the states'", answer, (a) => { a.state_count = 2; }, /lists 1 states/],
    ["a first date that is not the first state's", several, (a) => { a.history_begins = "2025-01-01"; }, /first and last states' dates/],
    ["a title of another expression", answer, (a) => { a.titles = [{ language: "fra", expression_iri: "http://elsewhere/fr", titles: [], short_titles: [] }]; }, /not an expression of this dossier's states/],
    ["a title in another language than asked", answer, (a) => { a.titles = [{ language: "deu", expression_iri: a.states[0].expression_iri, titles: [], short_titles: [] }]; }, /is in deu, and the dossier was asked in fra/],
    ["a language asked that is not held", answer, (a) => { a.requested_language = "eng"; }, /not one the work is held in/],
    ["a state in another language than asked", several, (a) => { a.requested_language = "fra"; }, /is in deu, and the dossier was asked in fra/],
    ["a state in a language not held", answer, (a) => { a.available_languages = ["deu"]; a.requested_language = null; }, /not said to be held in/],
    ["an item not held twice", answer, (a) => { a.not_held.push(structuredClone(a.not_held[0])); }, /names .* twice/],
    ["a state with no article", answer, (a) => { a.states[0].article_count = 0; }, /holds no article/],
    ["no state", answer, (a) => { a.states = []; a.state_count = 0; }, /at least one held state/],
    ["a missing member", answer, (a) => { delete a.states[0].stable_coordinate; }, /state 1 does not carry stable_coordinate/],
    ["a digest the census left unfilled", answer, (a) => { a.index_sha256 = PLACEHOLDER; }, /index_sha256 is not a SHA-256 digest/],
  ];
  for (const [what, base, change, reason] of cases) {
    assert.throws(() => readDossier(mutate(base, change)), reason, what);
  }
});

test("the pre-V3 dossier shape is not read as a V3 answer", () => {
  const retired = { identity: { lex_id: "lu-legilux:x" }, dates: [], status: { binding_status: "in_force" }, coverage: {}, slots: [] };
  assert.throws(() => readDossier(retired), /does not carry publisher/);
});
