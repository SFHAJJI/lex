// The launch contract's accessibility and scope lines, held on the live screens:
//   "no meaning by colour alone, linear diff, explicit dates, bracket tables whole".
//
// Measured on what the pages really render: every census answer of the eight screens through its view,
// and each page as the server renders it. Colour needs computed styles, so its check is the journey's
// (`journey.mjs`, in a real browser); these are the rest.
//
// "Bracket tables whole" is the product spec's rule 11 (`33-product-spec.md`): "Bracket tables and
// condition lists are always rendered whole; the UI offers no control that filters a bracket by a
// user-entered personal fact." So every quotation is the publisher's whole article text, and no form
// on a live page takes anything but the declared fields.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import * as app from "../.react-build/app.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));

const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'", "&#39;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27|#39);/g, (entity) => ENTITIES[entity]);
const textOf = (markup) => decode(markup.replace(/<script[\s\S]*?<\/script>/g, "").replace(/<[^>]+>/g, " "));

/** The answer census writes run-varying digests as a placeholder; a fixed digest stands in for them. */
const fixed = (node) => {
  if (node === "<varies-per-run>") return "a".repeat(64);
  if (Array.isArray(node)) return node.map(fixed);
  if (node !== null && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, fixed(value)]));
  return node;
};

const SCREENS = [
  ["coverage", "coverage", "live-coverage.mjs", "coverageOutcome", app.CoverageAnswerView],
  ["search", "search", "live-search.mjs", "searchOutcome", app.SearchAnswerView],
  ["dossier", "dossier", "live-dossier.mjs", "dossierOutcome", app.DossierAnswerView],
  ["reading", "evidence_bundle", "live-reading.mjs", "readingOutcome", app.ReadingAnswerView],
  ["history", "article_history", "live-history.mjs", "historyOutcome", app.HistoryAnswerView],
  ["compare", "diff", "live-compare.mjs", "compareOutcome", app.CompareAnswerView],
  ["radar", "changes_in_period", "live-radar.mjs", "radarOutcome", app.RadarAnswerView],
  ["export", "evidence_bundle", "live-reading.mjs", "readingOutcome", app.ExportAnswerView],
];

const PAGES = {
  coverage: app.renderLiveCoveragePage, search: app.renderLiveSearchPage, dossier: app.renderLiveDossierPage,
  reading: app.renderLiveReadingPage, history: app.renderLiveHistoryPage, compare: app.renderLiveComparePage,
  radar: app.renderLiveRadarPage, export: app.renderLiveExportPage,
};

/** Every census envelope of every screen, rendered through its view, answers and refusals alike. */
async function renders() {
  const found = [];
  for (const [name, operation, module, outcomeName, View] of SCREENS) {
    const scripts = await import(new URL(`../scripts/${module}`, import.meta.url).href);
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation)) {
      const outcome = scripts[outcomeName]({ state: entry.envelope.refusal ? "refusal" : "success", envelope: entry.envelope });
      found.push({ name, scenario: entry.scenario, envelope: entry.envelope, outcome, markup: renderToStaticMarkup(h(View, { outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} })) });
    }
  }
  return found;
}

// A date written with a month's name has a number beside it ("1 May 2024", "May 2024"); the word alone
// is an ordinary word ("may lie in the future").
const MONTH_NAMES = "January|February|March|April|May|June|July|August|September|October|November|December|janvier|février|mars|avril|mai|juin|juillet|août|septembre|octobre|novembre|décembre";
const MONTHS = new RegExp(`\\b\\d{1,2}(?:er)?\\s+(?:${MONTH_NAMES})\\b|\\b(?:${MONTH_NAMES})\\s+\\d{1,4}\\b`, "i");
const RELATIVE = /\b(today|yesterday|tomorrow|\d+\s+(?:days?|weeks?|months?|years?)\s+ago|last (?:week|month|year)|next (?:week|month|year)|aujourd’hui|hier|demain)\b/i;
const NUMERIC_NOT_ISO = /\b\d{1,2}[./]\d{1,2}[./]\d{2,4}\b/;
const ISO = /\b(\d{4})-(\d{2})-(\d{2})\b/g;

/**
 * A render without the publisher's own words: its quotations and whatever is marked in its statute
 * language. Those are the law's text, whose dates are the law's to write; the rule is about the dates
 * the interface states.
 */
const withoutQuotations = (markup) => markup
  .replace(/<blockquote[\s\S]*?<\/blockquote>/g, "")
  .replace(/<span lang="[^"]*">[\s\S]*?<\/span>/g, "");

test("every date a live screen shows is an explicit calendar date, never relative and never a local format", async () => {
  const texts = [...(await renders()).map((one) => [`${one.name}: ${one.scenario}`, textOf(withoutQuotations(one.markup))]),
    ...Object.entries(PAGES).map(([name, render]) => [`${name} page`, textOf(withoutQuotations(render()))])];
  for (const [label, text] of texts) {
    assert.doesNotMatch(text, RELATIVE, `${label} says a date relative to now`);
    assert.doesNotMatch(text, MONTHS, `${label} names a month rather than writing the date`);
    assert.doesNotMatch(text, NUMERIC_NOT_ISO, `${label} writes a date in a local numeric format`);
    for (const [date, year, month, day] of text.matchAll(ISO)) {
      const parsed = new Date(`${date}T00:00:00Z`);
      assert.ok(!Number.isNaN(parsed.valueOf()) && parsed.getUTCFullYear() === Number(year) && parsed.getUTCMonth() + 1 === Number(month) && parsed.getUTCDate() === Number(day), `${label}: ${date} is not a calendar date`);
    }
  }
  assert.ok(texts.some(([, text]) => ISO.test(text)), "the renders do show dates");
});

test("the diff is linear: one list per language, each article one row, its status a word", () => {
  // Two different states, as the answer census samples them (the envelope census holds the same state
  // on both dates, which has no rows).
  const row = answers.sampled.filter((sample) => sample.operation === "diff")[1];
  const envelope = structuredClone(census.envelopes.find((entry) => entry.operation === "diff" && !entry.envelope.refusal).envelope);
  envelope.result.value = fixed(row.answer);
  const outcome = compareOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = renderToStaticMarkup(h(app.CompareAnswerView, { outcome }));
  const words = liveChrome().compare.status;
  for (const comparison of markup.split("<section data-comparison=").slice(1)) {
    assert.doesNotMatch(comparison, /<table/, "no side-by-side table: one column a reader follows from top to bottom");
    const rows = [...comparison.matchAll(/<li data-status="([a-z]+)">([\s\S]*?)<\/li>/g)];
    assert.ok(rows.length > 0, "the comparison lists its articles");
    for (const [, status, body] of rows) {
      assert.ok(textOf(body).includes(words[status]), `a ${status} row says "${words[status]}" in words`);
    }
    assert.equal((comparison.match(/<ol/g) ?? []).length, (comparison.match(/<\/ol>/g) ?? []).length);
  }
  assert.ok(!/style="[^"]*colou?r/i.test(markup), "no status is carried by an inline colour");
});

test("every quotation is the publisher's whole article text: bracket tables and condition lists are never cut", async () => {
  let quoted = 0;
  for (const one of (await renders()).filter((render) => (render.name === "reading" || render.name === "export") && !render.envelope.refusal)) {
    const expected = one.envelope.result.value.states.flatMap((state) => state.articles.filter((article) => article.text !== null && article.text !== undefined).map((article) => article.text));
    const shown = [...one.markup.matchAll(/<blockquote[^>]*>([\s\S]*?)<\/blockquote>/g)].map((match) => decode(match[1]));
    assert.deepEqual(shown, expected, `${one.name}: ${one.scenario}: every article quoted whole, in order, none dropped`);
    quoted += shown.length;
  }
  assert.ok(quoted >= 49, `the census readings quote their articles (${quoted})`);
});

test("no form on a live page takes a personal fact: only the declared fields, each labelled from the table", () => {
  const form = liveChrome().form;
  const declared = new Set([form.workIdentifier, form.workIdentifierOptional, form.phrase, form.date, form.from, form.to, form.articleId, form.language]);
  for (const [name, render] of Object.entries(PAGES)) {
    const markup = render().replaceAll("<!-- -->", "");
    const forms = [...markup.matchAll(/<form[\s\S]*?<\/form>/g)].map((match) => match[0]);
    if (name === "coverage") {
      assert.equal(forms.length, 0, "Trust and Coverage asks nothing of the reader");
      continue;
    }
    assert.equal(forms.length, 1, `${name}: one form`);
    const controls = [...forms[0].matchAll(/<label>([^<]*?)\s*<(input|select|textarea)([^>]*)>/g)];
    assert.ok(controls.length > 0, `${name}: the form's controls are labelled`);
    assert.equal(controls.length, (forms[0].match(/<(input|select|textarea)\b/g) ?? []).length, `${name}: every control sits in its label`);
    for (const [, label, tag, attributes] of controls) {
      assert.ok(declared.has(decode(label).trim()), `${name}: "${label}" is a declared field`);
      if (tag === "input") assert.match(attributes, /type="(text|search)"/, `${name}: "${label}" is text, never a number, a range or a choice that filters a bracket`);
    }
  }
});
