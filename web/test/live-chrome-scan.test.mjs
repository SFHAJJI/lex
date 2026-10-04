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
// Every surface of the live pages is scanned. Trust and Coverage's answer (`Coverage`) says the table's
// words, from their one English source, `coverage.mjs`. The refusal card is scanned: its words are the table's (from their one English
// source, `refusal-card.mjs`), and the sentence a refusal says is the checkpoint list's (#793), held in
// each screen's module, so in English it counts as data here. The evaluation card is scanned too: its
// words are the table's and its figures the card's. What an export will carry (its
// watermark, its JSON) is the file's content, shown as the file will hold it, not interface text.
//
// The French pages are scanned the same way, compiled for French with the French table and the French
// refusal sentences swapped for the pseudo-locale: there the refusal sentences are the table's too, and
// the only ASCII letters a French page may show are its data and the English it marks English
// (`lang="en"`: the platform's own phrases, and the runs of a sentence that have no French), which in
// turn may hold none of the table's words.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import test from "node:test";
import { dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

import { build } from "esbuild";

import { europeEnvelope } from "../scripts/europe-bundle-sample.mjs";

const require = createRequire(import.meta.url);
const { createElement: h } = require("react");
const { renderToStaticMarkup } = require("react-dom/server");

const web = new URL("../", import.meta.url);
const TABLE = fileURLToPath(new URL("scripts/live-chrome.mjs", web));
const OUT = fileURLToPath(new URL(".react-build/app-pseudo.mjs", web));
const OUT_FRENCH = fileURLToPath(new URL(".react-build/app-pseudo-fr.mjs", web));

/** The two lines of the chrome table's module the swap rewrites: the tables, and the refusal sentences' translations. */
const TABLES_LINE = "export const LIVE_CHROME = Object.freeze({ en: EN, fr: LIVE_CHROME_FR });";
const TRANSLATIONS_LINE = "export const REFUSAL_TRANSLATIONS = Object.freeze({ fr: REFUSALS_FR });";

/** A letter as its fullwidth form, so text from the table is told apart from text that is not. */
const PSEUDO_SOURCE = `
const __pseudoText = (text) => text.split(/(\\{[A-Za-z]+\\})/).map((part, index) => (index % 2 === 1 ? part
  : part.replace(/[A-Za-z]/g, (letter) => String.fromCodePoint(letter.codePointAt(0) + 0xfee0)))).join('');
const __pseudo = (node) => (typeof node === 'string' ? __pseudoText(node)
  : Object.freeze(Object.fromEntries(Object.entries(node).map(([key, value]) => [key, __pseudo(value)]))));
`;

/** A build plugin that rewrites the chrome table's module with `swap`, each line it rewrites found exactly once. */
function swapChrome(swap) {
  return {
    name: "pseudo-chrome",
    setup(builder) {
      builder.onLoad({ filter: /live-chrome\.mjs$/ }, async (args) => {
        if (args.path !== TABLE) return undefined;
        const source = await readFile(args.path, "utf8");
        for (const line of [TABLES_LINE, TRANSLATIONS_LINE]) {
          assert.equal(source.split(line).length, 2, `the module holds ${JSON.stringify(line)} once, as the swap expects`);
        }
        // `resolveDir`, so the table's own imports (the refusal card's words, the French table) resolve as they do unswapped.
        return { contents: swap(source), loader: "js", resolveDir: dirname(args.path) };
      });
    },
  };
}

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
    plugins: [swapChrome((source) => source.replace(TABLES_LINE, `${PSEUDO_SOURCE}\nexport const LIVE_CHROME = Object.freeze({ en: __pseudo(EN), fr: LIVE_CHROME_FR });`))],
  });
  return import(`${pathToFileURL(OUT).href}?${Date.now()}`);
}

/**
 * The pages compiled for French, as the live build compiles them, with the French table and the French refusal
 * sentences swapped for the pseudo-locale; with them, the screens' own modules, compiled for French too, so the
 * sentences a state says are the French a French page says.
 */
async function frenchPseudoBuild() {
  const exported = [
    ["live-search.mjs", "searchOutcome"], ["live-dossier.mjs", "dossierOutcome"], ["live-reading.mjs", "readingOutcome"],
    ["live-history.mjs", "historyOutcome"], ["live-compare.mjs", "compareOutcome"], ["live-radar.mjs", "radarOutcome"],
    ["live-coverage.mjs", "coverageOutcome"], ["live-export.mjs", "exportState, pinKey"],
  ];
  await build({
    stdin: {
      contents: ["export * from './app/index.jsx';", ...exported.map(([module, names]) => `export { ${names} } from './scripts/${module}';`)].join("\n"),
      resolveDir: fileURLToPath(web),
      sourcefile: "french-scan.jsx",
      loader: "jsx",
    },
    outfile: OUT_FRENCH,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
    plugins: [swapChrome((source) => source
      .replace(TABLES_LINE, `${PSEUDO_SOURCE}\nexport const LIVE_CHROME = Object.freeze({ en: EN, fr: __pseudo(LIVE_CHROME_FR) });`)
      .replace(TRANSLATIONS_LINE, "export const REFUSAL_TRANSLATIONS = Object.freeze({ fr: __pseudo(REFUSALS_FR) });"))],
  });
  return import(`${pathToFileURL(OUT_FRENCH).href}?${Date.now()}`);
}

/**
 * Every pin of an EU reading, each article of each wording, quoted or held without text: the EU export is scanned with
 * all of them, so its panel says every kind of line it has.
 */
const everyEuropePin = (view, pinKey) => new Set(view.wordings.flatMap((wording) => [...wording.articles, ...wording.articlesWithoutText]
  .map((article) => pinKey(wording.wordingSha256, article.publisherId))));

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
    else if (typeof node === "number" || typeof node === "boolean") found.add(String(node));
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

  // Every census answer and refusal each screen shows, through its view.
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
  const refused = new Set();
  for (const [name, operation, module, outcomeName, View] of [...screens, ["coverage", "coverage", "live-coverage.mjs", "coverageOutcome", app.CoverageAnswerView]]) {
    const scripts = await import(new URL(`scripts/${module}`, web).href);
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation && candidate.envelope.refusal)) {
      const outcome = scripts[outcomeName]({ state: "refusal", envelope: entry.envelope });
      // The card lists the payload as the platform sent it, each member under its own name (a driver
      // decision: the member names are the contract's, like the refusal code beside them).
      const members = Object.keys(entry.envelope.refusal.helpful_payload ?? {});
      scan(`${name}: ${entry.scenario}`, renderToStaticMarkup(h(View, { outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} })), dataOf(entry.envelope, members, [outcome.sentence]));
      refused.add(name);
    }
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
  // The EU reading (the hand-built EU bundle the web tests share, until the census captures one), on the reading screen
  // and in the export composer, with every article pinned.
  {
    const { readingOutcome } = await import(new URL("scripts/live-reading.mjs", web).href);
    const { exportState, pinKey } = await import(new URL("scripts/live-export.mjs", web).href);
    const envelope = europeEnvelope();
    const outcome = readingOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", "the EU reading reads");
    scan("reading: an EU original wording", renderToStaticMarkup(h(app.ReadingAnswerView, { outcome })), dataOf(envelope));
    const pins = everyEuropePin(outcome.view, pinKey);
    scan("export: an EU original wording", renderToStaticMarkup(h(app.ExportAnswerView, { outcome, pins, onPin: () => {} })), dataOf(envelope));
    const composed = exportState(outcome, pins);
    assert.equal(composed.state, "composed", "export: an EU original wording");
    scan("export panel: an EU original wording", renderToStaticMarkup(h(app.ExportPanel, { outcome, pins, onSave: () => {} })).replace(/<pre>[\s\S]*?<\/pre>/, ""), dataOf(envelope, composed.model));
  }
  assert.deepEqual([...answered].sort(), [...screens.map(([name]) => name), "coverage"].sort(), "every screen had a census answer to scan");
  assert.deepEqual([...refused].sort(), [...screens.map(([name]) => name), "coverage"].sort(), "every screen had a census refusal to scan");
  assert.deepEqual(found, [], `interface text outside the chrome table:\n${found.join("\n")}`);
});

/** A render with every element marked English (`lang="en"`) cut out of it: the rest, and the English, element by element. */
function withoutEnglish(markup) {
  const english = [];
  let rest = markup;
  while (rest.includes(' lang="en"')) {
    const { inside, outside } = split(rest, ' lang="en"');
    english.push(inside);
    rest = outside;
  }
  return { rest, english };
}

/** A letter of the pseudo-locale: the table's own words, fullwidth. */
const PSEUDO_LETTER = /[Ａ-Ｚａ-ｚ]/;

test("no interface text on a French page or a census answer bypasses the French table, and what it says in English is marked English", async () => {
  const app = await frenchPseudoBuild();
  const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
  const found = [];
  let marked = 0;
  const scan = (label, markup, data) => {
    const { rest, english } = withoutEnglish(markup);
    for (const bypass of bypasses(rest, data)) found.push(`${label}: ${bypass}`);
    for (const element of english) {
      marked += 1;
      if (PSEUDO_LETTER.test(textsOf(element).join(" "))) found.push(`${label}: the table's words marked English: ${element.slice(0, 120)}`);
    }
  };
  const pages = {
    coverage: app.renderLiveCoveragePage, search: app.renderLiveSearchPage, dossier: app.renderLiveDossierPage,
    reading: app.renderLiveReadingPage, history: app.renderLiveHistoryPage, compare: app.renderLiveComparePage,
    radar: app.renderLiveRadarPage, export: app.renderLiveExportPage,
  };
  for (const [name, render] of Object.entries(pages)) {
    const html = render();
    assert.match(html, /<html lang="fr"/, `${name}: compiled for French`);
    const { inside: nav, outside: rest } = split(html, 'data-locale-nav=""');
    scan(`${name} page`, rest, name === "coverage" ? dataOf(withoutVerdicts(app.CENSUS_EVALUATION_CARD)) : []);
    scan(`${name} locale navigation`, nav, dataOf(["English", "Français", "Deutsch", "Lëtzebuergesch"]));
  }

  // Every census answer and refusal, through the French views and the French screens' own sentences: a refusal's
  // sentence is not data here, since a French page says it in French. Each refusal is said a second time without
  // its absence evidence, so its card cannot be shown and the page says why, in English, with its hint in French.
  const screens = [
    ["search", "search", "searchOutcome", app.SearchAnswerView],
    ["dossier", "dossier", "dossierOutcome", app.DossierAnswerView],
    ["reading", "evidence_bundle", "readingOutcome", app.ReadingAnswerView],
    ["history", "article_history", "historyOutcome", app.HistoryAnswerView],
    ["compare", "diff", "compareOutcome", app.CompareAnswerView],
    ["radar", "changes_in_period", "radarOutcome", app.RadarAnswerView],
    ["export", "evidence_bundle", "readingOutcome", app.ExportAnswerView],
    ["coverage", "coverage", "coverageOutcome", app.CoverageAnswerView],
  ];
  const props = (outcome) => ({ outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} });
  let unshown = 0;
  for (const [name, operation, outcomeName, View] of screens) {
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation)) {
      if (entry.envelope.refusal) {
        const members = Object.keys(entry.envelope.refusal.helpful_payload ?? {});
        const outcome = app[outcomeName]({ state: "refusal", envelope: entry.envelope });
        scan(`${name}: ${entry.scenario}`, renderToStaticMarkup(h(View, props(outcome))), dataOf(entry.envelope, members));
        const unshowable = structuredClone(entry.envelope);
        delete unshowable.refusal.helpful_payload?.what_would_answer;
        delete unshowable.refusal.helpful_payload?.asserts_absence_of_law;
        const without = app[outcomeName]({ state: "refusal", envelope: unshowable });
        if (without.card === false) unshown += 1;
        scan(`${name}: ${entry.scenario}, its card unshowable`, renderToStaticMarkup(h(View, props(without))), dataOf(unshowable, members));
        continue;
      }
      const outcome = app[outcomeName]({ state: "success", envelope: entry.envelope });
      assert.equal(outcome.state, "success", `${name}: ${entry.scenario}`);
      scan(`${name}: ${entry.scenario}`, renderToStaticMarkup(h(View, props(outcome))), dataOf(entry.envelope));
      if (name === "export") {
        const pins = new Set(outcome.view.states.flatMap((held) => [...held.articles, ...held.articlesWithoutText].map((article) => app.pinKey(held.stateSha256, article.publisherId))));
        const composed = app.exportState(outcome, pins);
        assert.equal(composed.state, "composed", `export: ${entry.scenario}`);
        scan(`export panel: ${entry.scenario}`, renderToStaticMarkup(h(app.ExportPanel, { outcome, pins, onSave: () => {} })).replace(/<pre>[\s\S]*?<\/pre>/, ""), dataOf(entry.envelope, composed.model));
      }
    }
    // The states with no French at all (a transport failure, an answer the page cannot read): English, marked so.
    for (const asked of [{ state: "transport_failure", code: "network_error" }, { state: "invalid_envelope", reason: "the answer is not one this page reads" }]) {
      scan(`${name}: ${asked.state}`, renderToStaticMarkup(h(View, props(app[outcomeName](asked)))), []);
    }
  }
  {
    const envelope = europeEnvelope();
    const outcome = app.readingOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", "the EU reading reads");
    const reading = renderToStaticMarkup(h(app.ReadingAnswerView, { outcome }));
    scan("reading: an EU original wording", reading, dataOf(envelope));
    // The annex line says the platform's reason in English, marked English on the French page (review of #919).
    const [annexLine] = reading.match(/<p data-annexes-not-served="2"[^>]*>[\s\S]*?<\/p>/) ?? [];
    const { reason } = envelope.result.value.wordings[0].annexes_not_served[0];
    assert.ok(annexLine?.includes(`<span lang="en">${reason}</span>`), `the annex's reason is marked English on a French page: ${annexLine}`);
    const pins = everyEuropePin(outcome.view, app.pinKey);
    scan("export: an EU original wording", renderToStaticMarkup(h(app.ExportAnswerView, { outcome, pins, onPin: () => {} })), dataOf(envelope));
    const composed = app.exportState(outcome, pins);
    assert.equal(composed.state, "composed", "export: an EU original wording");
    scan("export panel: an EU original wording", renderToStaticMarkup(h(app.ExportPanel, { outcome, pins, onSave: () => {} })).replace(/<pre>[\s\S]*?<\/pre>/, ""), dataOf(envelope, composed.model));
  }
  assert.ok(unshown >= 3, `refusals whose card cannot be shown were said (${unshown})`);
  assert.ok(marked >= 50, `the platform's English was marked English (${marked} elements)`);
  assert.deepEqual(found, [], `text on a French page outside the French table, or the table's words marked English:\n${found.join("\n")}`);
});
