// The checkpoint list of refusal sentences (Decision 95, ruling 4), held to what the live pages say.
//
// Every English sentence a live page says for a refusal must have exactly one French sentence (the
// reviewed French the French pages say, `live-chrome-fr.mjs`), no French sentence may stand for one the
// pages no longer say, each French template carries the placeholders its English carries, the printed
// list has no row without its French, and nothing in the product imports the list (it imports every
// screen). A missing French sentence fails here; on a page it throws rather than being said in English.

import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

import {
  FRENCH_HINTS,
  FRENCH_SENTENCES,
  FRENCH_TEMPLATES,
  SERVED_HINTS,
  SCREENS,
  renderCheckpointList,
  servedRefusalSentences,
  servedRefusalTemplates,
} from "../scripts/refusal-sentences.mjs";
import { refusalSentence } from "../scripts/live-chrome.mjs";

test("every served refusal sentence has one French sentence, and every French sentence a served sentence", () => {
  const served = servedRefusalSentences().map((row) => row.sentence);
  assert.equal(new Set(served).size, served.length, "each sentence is listed once");
  assert.deepEqual(served.filter((sentence) => FRENCH_SENTENCES[sentence] === undefined), [], "no served sentence lacks its French");
  assert.deepEqual(Object.keys(FRENCH_SENTENCES).filter((sentence) => !served.includes(sentence)), [], "no French sentence stands for one the pages no longer say");
  assert.ok(served.includes("This build has no Luxembourg index mounted.") && served.includes("This build has no EU index mounted."), "each index a payload can name");
  for (const sentence of served) assert.equal(refusalSentence(sentence, "fr"), FRENCH_SENTENCES[sentence], "a French page says the listed French");
  assert.throws(() => refusalSentence("A sentence the pages started to say.", "fr"), /no reviewed "fr" wording/, "a sentence with no French is never said in English in its place");
});

test("each page's two code-only sentences have French with the same placeholders", () => {
  const templates = servedRefusalTemplates();
  assert.deepEqual(templates.map((row) => row.screen), SCREENS.map((screen) => screen.id));
  assert.deepEqual(Object.keys(FRENCH_TEMPLATES), SCREENS.map((screen) => screen.id));
  for (const row of templates) {
    for (const kind of ["unexpected", "unshown"]) {
      const placeholders = (text) => [...text.matchAll(/\{[a-z]+\}/g)].map((match) => match[0]);
      assert.deepEqual(placeholders(FRENCH_TEMPLATES[row.screen][kind]), placeholders(row[kind]), `${row.screen} ${kind}`);
    }
  }
});

test("the printed list carries every sentence with its French, and no row without it", () => {
  const list = renderCheckpointList();
  for (const row of servedRefusalSentences()) assert.ok(list.includes(`| ${row.sentence} | ${FRENCH_SENTENCES[row.sentence]} |`), row.sentence);
  assert.ok(!list.includes("(no French)"));
  assert.match(list, /reviewed by Claude \(AI reviewer\), under the/);
});

test("the hints a card that cannot be shown still carries are in the list, as the pages say them (review of #793)", async () => {
  const { readingOutcome } = await import("../scripts/live-reading.mjs");
  const { compareOutcome } = await import("../scripts/live-compare.mjs");
  const { historyOutcome } = await import("../scripts/live-history.mjs");
  const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
  // An absence whose card cannot be shown: the census refusal without the absence evidence the card requires.
  const unshowable = (operation, scenarioStart) => {
    const envelope = structuredClone(census.envelopes.find((entry) => entry.operation === operation && entry.scenario.startsWith(scenarioStart)).envelope);
    delete envelope.refusal.helpful_payload.what_would_answer;
    delete envelope.refusal.helpful_payload.asserts_absence_of_law;
    return envelope;
  };
  const fill = (template, values) => Object.entries(values).reduce((text, [key, value]) => text.replace(`{${key}}`, value), template);
  const [historyBegins, nearestIds] = SERVED_HINTS;
  const cases = [
    [readingOutcome, unshowable("evidence_bundle", "a date before the work's history"), historyBegins, (payload) => ({ date: payload.history_begins })],
    [compareOutcome, unshowable("diff", "a from date before the work's history"), historyBegins, (payload) => ({ date: payload.history_begins })],
    [historyOutcome, unshowable("article_history", "an article id no held state carries"), nearestIds, (payload) => ({ ids: payload.nearest_anchors.join(", ") })],
  ];
  for (const [outcome, envelope, hint, values] of cases) {
    const result = outcome({ state: "refusal", envelope });
    assert.equal(result.card, false);
    assert.ok(result.sentence.endsWith(` ${fill(hint.template, values(envelope.refusal.helpful_payload))}`), `${hint.code}: the page says the listed hint (${result.sentence})`);
  }
  const list = renderCheckpointList();
  for (const hint of SERVED_HINTS) {
    assert.ok(list.includes(`| ${hint.template} | ${FRENCH_HINTS[hint.template]} |`), hint.template);
    const placeholders = (text) => [...text.matchAll(/\{[a-z]+\}/g)].map((match) => match[0]);
    assert.deepEqual(placeholders(FRENCH_HINTS[hint.template]), placeholders(hint.template));
  }
  assert.deepEqual(Object.keys(FRENCH_HINTS).sort(), SERVED_HINTS.map((hint) => hint.template).sort(), "no French hint stands for a hint the pages no longer say");
});

test("nothing the product ships imports the list", async () => {
  for (const directory of ["../app/", "../scripts/"]) {
    for (const name of await readdir(new URL(directory, import.meta.url))) {
      if (!/\.(mjs|jsx)$/.test(name) || name === "refusal-sentences.mjs") continue;
      const source = await readFile(new URL(`${directory}${name}`, import.meta.url), "utf8");
      // An import of the module, not a mention of it: a comment may name the list's file. The list imports every
      // screen, so a screen importing it would import itself.
      assert.doesNotMatch(source, /(?:from\s+|import\s*\(\s*)["'][^"']*refusal-sentences(?:\.mjs)?["']/, `${name} imports the checkpoint list`);
    }
  }
});
