// The vocabulary check (the specification of record's R21) holds the Compare card: never "in force", "en vigueur" or
// "applied" of a date or an interval, in any language. The card stopped saying "the same version applied on both dates"
// (the plan's item 0.6): one state on both dates is now "Both dates fall within the same version, applicable from {date}
// (publisher)" for Luxembourg, and for the EU (when Compare serves it, the plan's item 2.5) "Both dates fall within the
// same consolidated wording state (from {date})".
//
// The check is `assertTimeVocabulary` (`publisher-vocabulary.mjs`). It is held to every word the Compare page says of
// its own, in English and in the reviewed French, and to the card as rendered for every census comparison; and it is
// shown failing on purpose: the old note, fed to it as a string and as a rendered card, is refused.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CompareAnswerView, CompareView } from "../.react-build/app.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";
import { readDiff } from "../scripts/compare-answer.mjs";
import { LIVE_CHROME, entriesOf, fillText } from "../scripts/live-chrome.mjs";
import { FORBIDDEN_TIME_WORDS, SAME_STATE_SENTENCE, assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));

const OLD_NOTE = "the same version applied on both dates";

// What a card says, its tags taken out.
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/\s+/g, " ");

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

test("the check refuses each word R21 forbids, in English and in French, and lets the publisher's own words through", () => {
  for (const said of [
    "The same version applied on both dates.", "in force from 2024-02-01", "En vigueur depuis le 2024-02-01",
    "les versions qui s’appliquaient à ces dates", "la version appliquée", "la loi s'appliquait",
  ]) {
    assert.throws(() => assertTimeVocabulary(said, "a test"), /R21 never says/, said);
  }
  for (const said of [
    "Both dates fall within the same version, applicable from 2024-02-01 (publisher).",
    "Les deux dates relèvent de la même version, applicable à partir du 2024-02-01 (éditeur).",
    "la version applicable à partir du 2024-02-01", "S’applique à partir du", "the state applying from 2024-02-01",
    "an end-of-force date", "une date de fin de vigueur", "applicability",
  ]) {
    assert.equal(assertTimeVocabulary(said, "a test"), said);
  }
});

test("the old note is refused: by the check, by the reader, and as a rendered card", () => {
  // Fed to the check as it was said.
  assert.throws(() => assertTimeVocabulary(OLD_NOTE, "the old Compare note"), /says "applied"/);

  // Sent by a platform that still said it, the reader refuses the answer as drift.
  const sample = answers.sampled.find((entry) => entry.operation === "diff" && entry.answer.comparisons[0].same_state);
  const old = withDigests(structuredClone(sample.answer));
  old.comparisons[0].note = OLD_NOTE;
  assert.throws(() => readDiff(old), /says "the same version applied on both dates" rather than "Both dates fall within the same version/);

  // And a card that printed it, fed to the check, fails it.
  const view = readDiff(withDigests(structuredClone(answers.sampled.filter((entry) => entry.operation === "diff")[1].answer)));
  const printed = { ...view, comparisons: view.comparisons.map((comparison) => ({ ...comparison, note: OLD_NOTE })) };
  const markup = renderToStaticMarkup(h(CompareView, { view: printed }));
  assert.ok(text(markup).includes("The same version applied on both dates."), "the card prints the note it is given");
  assert.throws(() => assertTimeVocabulary(text(markup), "the Compare card"), /says "applied"/);
});

test("every word the Compare page says of its own passes the check, in English and in French", () => {
  for (const language of ["en", "fr"]) {
    for (const [path, said] of entriesOf(LIVE_CHROME[language].compare)) assertTimeVocabulary(said, `${language} compare.${path}`);
    assertTimeVocabulary(fillText(LIVE_CHROME[language].compare.sameVersion, { from: "2024-02-01" }), `${language} compare.sameVersion`);
  }
  assert.equal(fillText(LIVE_CHROME.en.compare.sameVersion, { from: "2024-02-01" }), `${SAME_STATE_SENTENCE.publisher_applicability("2024-02-01")}.`,
    "the page's English is the platform's note, as a sentence");
  // The EU's form, for when Compare serves EU versions: its consolidated wording state, never a Luxembourg word.
  const europe = SAME_STATE_SENTENCE.official_consolidation_state("2016-04-27");
  assert.equal(europe, "Both dates fall within the same consolidated wording state (from 2016-04-27)");
  assertTimeVocabulary(europe, "the EU same-state sentence");
  assert.doesNotMatch(europe, /\bversion\b|applicab|\(publisher\)/);
});

test("the Compare card passes the check for every census comparison, and one state says the publisher's clock", () => {
  const entries = census.envelopes.filter((entry) => entry.operation === "diff" && entry.envelope.result);
  assert.ok(entries.length > 0, "the census holds a comparison");
  for (const entry of entries) {
    const outcome = compareOutcome({ state: "success", envelope: entry.envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const said = text(renderToStaticMarkup(h(CompareAnswerView, { outcome })));
    assertTimeVocabulary(said, `the Compare card for ${entry.scenario}`);
    for (const comparison of outcome.view.comparisons.filter((candidate) => candidate.sameState)) {
      assert.ok(said.includes(`Both dates fall within the same version, applicable from ${comparison.from.applicabilityDate} (publisher).`), entry.scenario);
    }
  }
  // The census's two-state comparison too, which keeps the platform's own note.
  const two = withDigests(structuredClone(answers.sampled.filter((entry) => entry.operation === "diff")[1].answer));
  assertTimeVocabulary(text(renderToStaticMarkup(h(CompareView, { view: readDiff(two) }))), "the two-state Compare card");
  assert.ok(FORBIDDEN_TIME_WORDS.test(OLD_NOTE));
});
