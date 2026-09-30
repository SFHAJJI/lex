// The V3 evidence bundle reader, held to the answer the real handler sent.
//
// The answer comes from `schemas/v3-platform/answer-samples.json` (operation `evidence_bundle`),
// captured by driving the real handler on the fixture mount: one state in French, 49 quoted articles.
// A bundle in two languages is built from it the way the producer builds one (one state per served
// language), and each rule the answer states about itself is then broken once.

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { ADMITTING_RIGHTS_DISPOSITION, readEvidenceBundle } from "../scripts/reading-answer.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

async function capturedAnswer() {
  const parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  const rows = parsed.sampled.filter((sample) => sample.operation === "evidence_bundle");
  assert.equal(rows.length, 1, "the census holds the evidence bundle this reader is written against");
  assert.equal(rows[0].object_type, "evidence_bundle");
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

/** The captured bundle with a German state beside the French one, as the producer selects one per language. */
function twoLanguages(base) {
  return mutate(base, (answer) => {
    const german = structuredClone(answer.states[0]);
    const sha = "d".repeat(64);
    german.language = "deu";
    german.state_sha256 = sha;
    german.expression_iri = german.expression_iri.replace(/\/fr$/, "/de");
    german.permalink = `${german.stable_coordinate}--${sha}`;
    for (const article of german.articles) {
      article.language = "deu";
      article.article_permalink = `${german.permalink}#${article.publisher_id}`;
    }
    answer.states.push(german);
    answer.available_languages = ["deu", "fra"];
    answer.requested_language = null;
  });
}

test("the captured bundle reads: one state, its quoted articles, and what the bundle does not hold", async () => {
  const answer = await capturedAnswer();
  const view = readEvidenceBundle(answer);
  assert.equal(view.rightsDisposition, ADMITTING_RIGHTS_DISPOSITION);
  assert.equal(view.states.length, 1);
  const [state] = view.states;
  assert.equal(state.articles.length, 49);
  assert.equal(state.articlesNotAdmitted, 5);
  assert.equal(state.validityConflictCount, state.articles.filter((article) => article.validityConflict).length);
  assert.ok(state.articles.some((article) => article.notes.length > 0), "the notes travel beside the text");
  for (const article of state.articles) {
    assert.equal(article.permalink, `${state.permalink}#${article.publisherId}`);
    assert.equal(createHash("sha256").update(article.text, "utf8").digest("hex"), article.textSha256,
      `${article.publisherId}: the text digest is the served text's`);
  }
  assert.deepEqual(view.notHeld.map((row) => row.item), answer.not_held.map((row) => row.item));
});

test("a bundle in two languages reads, one state per language", async () => {
  const view = readEvidenceBundle(twoLanguages(await capturedAnswer()));
  assert.deepEqual(view.states.map((state) => state.language), ["fra", "deu"]);
  assert.ok(view.states[1].articles.every((article) => article.language === "deu"));
});

test("each rule the bundle states about itself is refused when broken, with that rule's reason", async () => {
  const answer = await capturedAnswer();
  const two = twoLanguages(answer);
  const first = (a) => a.states[0];
  const cases = [
    ["another publisher's bundle", (a) => { a.publisher = "eu-eurlex"; }, /Luxembourg \(lu-legilux\) bundle only/],
    ["another rights disposition", (a) => { a.rights_disposition = "non_admitting_licence_scl"; }, /only under agreed_same_run_cc_by/],
    ["a source under another disposition", (a) => { first(a).sources[0].rights_disposition = "withheld"; }, /acquired under withheld/],
    ["a state applying after the date", (a) => { a.requested_date = "2024-01-31"; }, /after the date asked/],
    ["a state followed before the date", (a) => { first(a).next_applicability_date = "2024-02-01"; }, /does not apply on the date asked/],
    ["a coordinate of another date", (a) => { first(a).stable_coordinate = `/lu-legilux/${a.work_key}/2020-01-01`; }, /does not name its work and date/],
    ["a permalink to another digest", (a) => { first(a).permalink = first(a).permalink.slice(0, -1) + "0"; }, /does not pin its coordinate and digest/],
    ["an article permalink of another article", (a) => { first(a).articles[0].article_permalink += "x"; }, /not its state's permalink with its id/],
    ["a byte length that is not the text's", (a) => { first(a).articles[0].text_byte_length += 1; }, /states \d+ bytes of text and carries/],
    ["an article in another language", (a) => { first(a).articles[0].language = "deu"; }, /in a state held in fra/],
    ["a body that is not a source", (a) => { first(a).articles[0].body_sha256 = "e".repeat(64); }, /not one of its state's sources/],
    ["another official source", (a) => { first(a).articles[0].official_source = "http://elsewhere/eli"; }, /not at its state's official source/],
    ["a validity flag that does not follow the dates", (a) => { first(a).articles[0].validity_conflict = !first(a).articles[0].validity_conflict; }, /says validity_conflict is/],
    ["a conflict count that is not the articles'", (a) => { first(a).validity_conflict_count -= 1; }, /articles are flagged/],
    ["no quoted article", (a) => { first(a).articles = []; first(a).validity_conflict_count = 0; }, /quotes no article/],
    ["an article without text for another reason", (a) => { first(a).articles_without_text = [{ article_identity_sha256: "f".repeat(64), publisher_id: "art_x", reason: "withheld" }]; }, /no_text_tokens/],
    ["an article both quoted and without text", (a) => { first(a).articles_without_text = [{ article_identity_sha256: first(a).articles[0].article_identity_sha256, publisher_id: first(a).articles[0].publisher_id, reason: "no_text_tokens" }]; }, /twice, or both as quoted and as without text/],
    ["body digests that are not the sources'", (a) => { first(a).body_sha256s = ["e".repeat(64)]; }, /not its sources' bodies/],
    ["a language asked that is not held", (a) => { a.requested_language = "eng"; }, /not one the work is held in/],
    ["an item not held twice", (a) => { a.not_held.push(structuredClone(a.not_held[0])); }, /names .* twice/],
    ["a missing member", (a) => { delete first(a).articles[3].text_sha256; }, /article 4 does not carry text_sha256/],
    ["a digest the census left unfilled", (a) => { a.verified_by.index_sha256 = PLACEHOLDER; }, /verified_by.index_sha256 is not a SHA-256 digest/],
  ];
  for (const [what, change, reason] of cases) {
    assert.throws(() => readEvidenceBundle(mutate(answer, change)), reason, what);
  }
  assert.throws(() => readEvidenceBundle(mutate(two, (a) => { a.states[1].language = "fra"; for (const x of a.states[1].articles) x.language = "fra"; })), /two states in fra/);
  assert.throws(() => readEvidenceBundle(mutate(two, (a) => { a.requested_language = "fra"; })), /is in deu, and the bundle was asked in fra/);
  assert.throws(() => readEvidenceBundle(mutate(answer, (a) => { a.states = []; })), /none is refused, not answered/);
});

test("the pre-V3 reading shape is not read as a V3 bundle", () => {
  assert.throws(() => readEvidenceBundle({ work: { lex_id: "lu-legilux:x" }, provisions: [] }), /does not carry publisher/);
});
