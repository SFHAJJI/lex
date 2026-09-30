// No interface text on a live page bypasses the chrome table (the French chrome plan's completeness
// test; Decision 41).
//
// The pages are compiled a second time with the table swapped for a pseudo-locale: every letter of
// every entry, outside its `{placeholders}`, becomes its fullwidth form. Every page and every census
// answer is then rendered from that build, and whatever ASCII letters are left once the answer's own
// data is taken out (identifiers, dates, the platform's phrases) is interface text that did not come
// from the table, which a French table could never translate. The pages' structure is untouched by
// the swap, so the same render proves the table reaches every text node.
//
// Two surfaces carry copy of their own and are the next slices, so they are not scanned: the refusal
// card (a refusal's sentences are the checkpoint list of #793; the card's labels live in
// `refusal-card.mjs`, shared with the pre-V3 renderer) and Trust and Coverage's `Coverage` component.
// The evaluation card on that page is scanned: its words are the table's and its figures the card's. What an export will carry (its
// watermark, its JSON) is the file's content, shown as the file will hold it, not interface text.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

import { build } from "esbuild";

const require = createRequire(import.meta.url);
const { createElement: h } = require("react");
const { renderToStaticMarkup } = require("react-dom/server");

const web = new URL("../", import.meta.url);
const TABLE = fileURLToPath(new URL("scripts/live-chrome.mjs", web));
const OUT = fileURLToPath(new URL(".react-build/app-pseudo.mjs", web));

/** A letter as its fullwidth form, so text from the table is told apart from text that is not. */
const PSEUDO_SOURCE = `
const __pseudoText = (text) => text.split(/(\\{[A-Za-z]+\\})/).map((part, index) => (index % 2 === 1 ? part
  : part.replace(/[A-Za-z]/g, (letter) => String.fromCodePoint(letter.codePointAt(0) + 0xfee0)))).join('');
const __pseudo = (node) => (typeof node === 'string' ? __pseudoText(node)
  : Object.freeze(Object.fromEntries(Object.entries(node).map(([key, value]) => [key, __pseudo(value)]))));
`;

async function pseudoBuild() {
  await build({
    entryPoints: [fileURLToPath(new URL("app/index.jsx", web))],
    outfile: OUT,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    plugins: [{
      name: "pseudo-chrome",
      setup(builder) {
        builder.onLoad({ filter: /live-chrome\.mjs$/ }, async (args) => {
          if (args.path !== TABLE) return undefined;
          const source = await readFile(args.path, "utf8");
          const table = "export const LIVE_CHROME = Object.freeze({ en: EN });";
          assert.equal(source.split(table).length, 2, "the table is exported once, as the swap expects");
          return { contents: source.replace(table, `${PSEUDO_SOURCE}\nexport const LIVE_CHROME = Object.freeze({ en: __pseudo(EN) });`), loader: "js" };
        });
      },
    }],
  });
  return import(`${pathToFileURL(OUT).href}?${Date.now()}`);
}

const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'", "&#39;": "'", "&nbsp;": " " };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|nbsp|#x27|#39);/g, (entity) => ENTITIES[entity]);

/** Every text node and every human-read attribute of a render, decoded. */
function textsOf(markup) {
  const body = markup.replace(/<script[\s\S]*?<\/script>/g, "").replace(/<style[\s\S]*?<\/style>/g, "").replace(/<!--[\s\S]*?-->/g, "");
  const texts = [...body.matchAll(/>([^<]+)</g)].map((match) => decode(match[1]));
  const attributes = [...body.matchAll(/\s(?:placeholder|aria-label|title|alt)="([^"]*)"/g)].map((match) => decode(match[1]));
  return [...texts, ...attributes].filter((text) => text.trim() !== "");
}

/**
 * One element of a render, found by an attribute its opening tag carries, split from the rest: the
 * element with everything inside it, and the render without it.
 */
function split(markup, attribute) {
  const marker = markup.indexOf(attribute);
  if (marker < 0) throw new Error(`the render has no element with ${attribute}`);
  const start = markup.lastIndexOf("<", marker);
  const tag = markup.slice(start).match(/^<([a-z]+)/)[1];
  let depth = 0;
  for (const match of markup.slice(start).matchAll(new RegExp(`<(/?)${tag}[\\s>]`, "g"))) {
    depth += match[1] === "/" ? -1 : 1;
    if (depth === 0) {
      const end = start + match.index + `</${tag}>`.length;
      return { inside: markup.slice(start, end), outside: markup.slice(0, start) + markup.slice(end) };
    }
  }
  throw new Error(`the element with ${attribute} is never closed`);
}

/**
 * The evaluation card as the data it shows: every value but its verdicts, which the page says in the
 * table's words ("pass", "caught the shuffle"), so a verdict written into the page by hand is caught
 * (review of #807: the card's "pass" exempted a hard-coded "pass").
 */
function withoutVerdicts(node) {
  if (Array.isArray(node)) return node.map(withoutVerdicts);
  if (node === null || typeof node !== "object") return node;
  return Object.fromEntries(Object.entries(node).filter(([key]) => key !== "verdict").map(([key, value]) => [key, withoutVerdicts(value)]));
}

/** Every string and number an answer carries, longest first, so a longer value is taken out whole. */
function dataOf(...values) {
  const found = new Set();
  const walk = (node) => {
    if (typeof node === "string") found.add(node);
    else if (typeof node === "number") found.add(String(node));
    else if (Array.isArray(node)) node.forEach(walk);
    else if (node !== null && typeof node === "object") Object.values(node).forEach(walk);
  };
  values.forEach(walk);
  return [...found].filter((value) => value.length > 0).sort((a, b) => b.length - a.length);
}

/**
 * The text a render shows that came from neither the table nor the data: each ASCII letter run left
 * once the data is taken out and the table's (fullwidth) text is set aside. A platform phrase shown as
 * a sentence is capitalised and closed with a full stop, so its first letter is compared either way.
 */
function bypasses(markup, data) {
  const found = [];
  for (const text of textsOf(markup)) {
    let rest = text;
    for (const value of data) {
      rest = rest.split(value).join(" ");
      const capitalised = `${value.charAt(0).toUpperCase()}${value.slice(1)}`;
      if (capitalised !== value) rest = rest.split(capitalised).join(" ");
    }
    const runs = rest.match(/[A-Za-z]+/g);
    if (runs !== null) found.push(`${JSON.stringify(text.trim().slice(0, 80))}: ${runs.join(" ")}`);
  }
  return found;
}

test("no interface text on a live page or a census answer bypasses the chrome table", async () => {
  const app = await pseudoBuild();
  const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
  const found = [];
  const scan = (label, markup, data) => {
    for (const bypass of bypasses(markup, data)) found.push(`${label}: ${bypass}`);
  };

  // Each page as the server renders it: the shell, the form and the idle or loading state.
  const pages = {
    coverage: app.renderLiveCoveragePage, search: app.renderLiveSearchPage, dossier: app.renderLiveDossierPage,
    reading: app.renderLiveReadingPage, history: app.renderLiveHistoryPage, compare: app.renderLiveComparePage,
    radar: app.renderLiveRadarPage, export: app.renderLiveExportPage,
  };
  for (const [name, render] of Object.entries(pages)) {
    // The locale navigation names each language in that language, which no table translates. Only
    // there: a language named anywhere else is the interface's word for it (review of #806: the
    // statute-language selects' "English" hid behind a page-wide exemption). The evaluation card's own
    // values (its target sentence, set names, figures) are the card's.
    const { inside: nav, outside: rest } = split(render(), 'data-locale-nav=""');
    scan(`${name} page`, rest, name === "coverage" ? dataOf(withoutVerdicts(app.CENSUS_EVALUATION_CARD)) : []);
    scan(`${name} locale navigation`, nav, dataOf(["English", "Français", "Deutsch", "Lëtzebuergesch"]));
  }

  // Every census answer each screen shows, through its view. Refusals are the checkpoint list's and the
  // card's, so only answers are scanned here.
  const screens = [
    ["search", "search", "live-search.mjs", "searchOutcome", app.SearchAnswerView],
    ["dossier", "dossier", "live-dossier.mjs", "dossierOutcome", app.DossierAnswerView],
    ["reading", "evidence_bundle", "live-reading.mjs", "readingOutcome", app.ReadingAnswerView],
    ["history", "article_history", "live-history.mjs", "historyOutcome", app.HistoryAnswerView],
    ["compare", "diff", "live-compare.mjs", "compareOutcome", app.CompareAnswerView],
    ["radar", "changes_in_period", "live-radar.mjs", "radarOutcome", app.RadarAnswerView],
    ["export", "evidence_bundle", "live-reading.mjs", "readingOutcome", app.ExportAnswerView],
  ];
  const answered = new Set();
  for (const [name, operation, module, outcomeName, View] of screens) {
    const scripts = await import(new URL(`scripts/${module}`, web).href);
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation && !candidate.envelope.refusal)) {
      const outcome = scripts[outcomeName]({ state: "success", envelope: entry.envelope });
      assert.equal(outcome.state, "success", `${name}: ${entry.scenario}`);
      const data = dataOf(entry.envelope);
      scan(`${name}: ${entry.scenario}`, renderToStaticMarkup(h(View, { outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} })), data);
      answered.add(name);
      if (name === "export") {
        const { exportState, pinKey } = await import(new URL("scripts/live-export.mjs", web).href);
        const pins = new Set(outcome.view.states.flatMap((held) => [...held.articles, ...held.articlesWithoutText].map((article) => pinKey(held.stateSha256, article.publisherId))));
        const panel = renderToStaticMarkup(h(app.ExportPanel, { outcome, pins, onSave: () => {} }));
        const composed = exportState(outcome, pins);
        assert.equal(composed.state, "composed", `export: ${entry.scenario}`);
        // The panel shows what the file will carry (its watermark, its rule, its JSON), which is the
        // file's content rather than interface text.
        scan(`export panel: ${entry.scenario}`, panel.replace(/<pre>[\s\S]*?<\/pre>/, ""), dataOf(entry.envelope, composed.model));
      }
    }
  }
  assert.deepEqual([...answered].sort(), screens.map(([name]) => name).sort(), "every screen had a census answer to scan");
  assert.deepEqual(found, [], `interface text outside the chrome table:\n${found.join("\n")}`);
});
