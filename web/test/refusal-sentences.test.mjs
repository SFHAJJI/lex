// The checkpoint list of refusal sentences (Decision 95, ruling 4), held to what the live pages say.
//
// Every English sentence a live page says for a refusal must have exactly one French draft, no draft may
// stand for a sentence the pages no longer say, each template draft carries the placeholders its English
// carries, the printed list has no row without a draft, and nothing in the product imports the drafts:
// no French string ships before it is reviewed.

import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

import {
  FRENCH_DRAFTS,
  FRENCH_TEMPLATE_DRAFTS,
  SCREENS,
  renderCheckpointList,
  servedRefusalSentences,
  servedRefusalTemplates,
} from "../scripts/refusal-sentences.mjs";

test("every served refusal sentence has one French draft, and every draft a served sentence", () => {
  const served = servedRefusalSentences().map((row) => row.sentence);
  assert.equal(new Set(served).size, served.length, "each sentence is listed once");
  assert.deepEqual(served.filter((sentence) => FRENCH_DRAFTS[sentence] === undefined), [], "no served sentence lacks a draft");
  assert.deepEqual(Object.keys(FRENCH_DRAFTS).filter((sentence) => !served.includes(sentence)), [], "no draft stands for a sentence the pages no longer say");
  assert.ok(served.includes("This build has no Luxembourg index mounted.") && served.includes("This build has no EU index mounted."), "each index a payload can name");
});

test("each page's two code-only sentences have drafts with the same placeholders", () => {
  const templates = servedRefusalTemplates();
  assert.deepEqual(templates.map((row) => row.screen), SCREENS.map((screen) => screen.id));
  assert.deepEqual(Object.keys(FRENCH_TEMPLATE_DRAFTS), SCREENS.map((screen) => screen.id));
  for (const row of templates) {
    for (const kind of ["unexpected", "unshown"]) {
      const placeholders = (text) => [...text.matchAll(/\{[a-z]+\}/g)].map((match) => match[0]);
      assert.deepEqual(placeholders(FRENCH_TEMPLATE_DRAFTS[row.screen][kind]), placeholders(row[kind]), `${row.screen} ${kind}`);
    }
  }
});

test("the printed list carries every sentence with its draft, and no row without one", () => {
  const list = renderCheckpointList();
  for (const row of servedRefusalSentences()) assert.ok(list.includes(`| ${row.sentence} | ${FRENCH_DRAFTS[row.sentence]} |`), row.sentence);
  assert.ok(!list.includes("(no draft)"));
});

test("nothing the product ships imports the drafts", async () => {
  for (const directory of ["../app/", "../scripts/"]) {
    for (const name of await readdir(new URL(directory, import.meta.url))) {
      if (!/\.(mjs|jsx)$/.test(name) || name === "refusal-sentences.mjs") continue;
      const source = await readFile(new URL(`${directory}${name}`, import.meta.url), "utf8");
      assert.ok(!source.includes("refusal-sentences"), `${name} imports the unreviewed French drafts`);
    }
  }
});
