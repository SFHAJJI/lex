// The live pages' interface copy comes from one table (`live-chrome.mjs`), so a language added to the
// interface is one reviewed table beside English, not copy scattered through the pages (Decision 41).
//
// Each page must render exactly its table's title, eyebrow, heading and introduction, and the table
// must answer only for a reviewed language: asking it for any other is a substitution, refused.

import assert from "node:assert/strict";
import test from "node:test";

import {
  renderLiveComparePage,
  renderLiveCoveragePage,
  renderLiveDossierPage,
  renderLiveExportPage,
  renderLiveHistoryPage,
  renderLiveRadarPage,
  renderLiveReadingPage,
  renderLiveSearchPage,
} from "../.react-build/app.mjs";
import { LIVE_CHROME, countedEntry, fillCounted, fillParts, fillText, liveChrome } from "../scripts/live-chrome.mjs";
import { REVIEWED_CHROME_LOCALES } from "../scripts/locale-unavailable.mjs";

const PAGES = {
  coverage: renderLiveCoveragePage,
  search: renderLiveSearchPage,
  dossier: renderLiveDossierPage,
  reading: renderLiveReadingPage,
  history: renderLiveHistoryPage,
  compare: renderLiveComparePage,
  radar: renderLiveRadarPage,
  export: renderLiveExportPage,
};

// React escapes an apostrophe in text; the table holds the text itself.
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

test("every live page renders its chrome from the table", () => {
  const copy = liveChrome("en");
  assert.deepEqual(Object.keys(copy).filter((key) => key !== "form" && key !== "common").sort(), Object.keys(PAGES).sort(), "one entry per live page, the forms, and what the screens share");
  for (const [key, render] of Object.entries(PAGES)) {
    const html = render();
    const entry = copy[key];
    assert.ok(html.includes(`<title>${escaped(entry.title)} - Lex V3 live</title>`), `${key}: title`);
    assert.ok(html.includes(`<p class="eyebrow">${escaped(entry.eyebrow)}</p>`), `${key}: eyebrow`);
    assert.ok(html.includes(`<h1>${escaped(entry.heading)}</h1>`), `${key}: heading`);
    assert.ok(html.includes(`<p>${escaped(entry.intro)}</p>`), `${key}: introduction`);
  }
});

test("every form's labels and button come from the table", () => {
  const { form } = liveChrome("en");
  for (const [key, render] of Object.entries(PAGES)) {
    if (key === "coverage") continue;
    // A page rendered for hydration marks text boundaries with empty comments; they are not text.
    const html = render().replaceAll("<!-- -->", "");
    assert.ok(html.includes(`<button type="submit">${escaped(form.submit[key])}</button>`), `${key}: the submit button`);
    assert.ok(html.includes(`<option value="">${escaped(form.anyLanguage)}</option>`) || html.includes(`<option value="" selected="">${escaped(form.anyLanguage)}</option>`) || key === "search", `${key}: the blank language option`);
    assert.ok(html.includes(`${escaped(form.language)} <select`), `${key}: the language label`);
  }
  assert.ok(renderLiveRadarPage().replaceAll("<!-- -->", "").includes(`${escaped(form.workIdentifierOptional)} <input`), "radar's optional identifier");
  assert.ok(renderLiveHistoryPage().replaceAll("<!-- -->", "").includes(`${escaped(form.articleId)} <input`), "history's article id");
  assert.ok(renderLiveSearchPage().replaceAll("<!-- -->", "").includes(`${escaped(form.phrase)} <input`), "search's phrase");
});

test("the French draft has the English table's exact shape, and the product never imports it", async () => {
  const { LIVE_CHROME_FR_DRAFT, entriesOf, renderChromeDraft } = await import("../scripts/live-chrome-fr-draft.mjs");
  const english = entriesOf(liveChrome("en")).map(([path]) => path);
  const french = entriesOf(LIVE_CHROME_FR_DRAFT).map(([path]) => path);
  assert.deepEqual(french, english, "one draft per English entry, in order, and none for an entry the table no longer has");
  for (const [path, text] of entriesOf(LIVE_CHROME_FR_DRAFT)) assert.ok(text.trim() === text && text.length > 0, path);
  assert.ok(!renderChromeDraft().includes("(no draft)"));
  const { readdir, readFile } = await import("node:fs/promises");
  for (const directory of ["../app/", "../scripts/"]) {
    for (const name of await readdir(new URL(directory, import.meta.url))) {
      if (!/\.(mjs|jsx)$/.test(name) || name === "live-chrome-fr-draft.mjs") continue;
      // An import of the module, not a mention of it: a comment may name the draft's file.
      assert.doesNotMatch(await readFile(new URL(`${directory}${name}`, import.meta.url), "utf8"), /(?:from\s+|import\s*\(\s*)["'][^"']*live-chrome-fr-draft(?:\.mjs)?["']/, `${name} imports the unreviewed French draft`);
    }
  }
});

test("each draft template has its English template's placeholders, and each counted entry both forms", async () => {
  const { LIVE_CHROME_FR_DRAFT, entriesOf } = await import("../scripts/live-chrome-fr-draft.mjs");
  const french = new Map(entriesOf(LIVE_CHROME_FR_DRAFT));
  const placeholders = (text) => [...text.matchAll(/\{([A-Za-z]+)\}/g)].map((match) => match[1]).sort();
  for (const [path, english] of entriesOf(liveChrome("en"))) {
    assert.deepEqual(placeholders(french.get(path)), placeholders(english), `${path}: the draft says what the English says`);
  }
  // A counted entry is an object whose keys are plural forms; each language needs "one" and "other".
  const counted = (table, path = "") => Object.entries(table).flatMap(([key, value]) => {
    if (typeof value === "string") return [];
    if (Object.keys(value).includes("one")) return [[`${path}${key}`, value]];
    return counted(value, `${path}${key}.`);
  });
  const englishCounted = counted(liveChrome("en"));
  assert.ok(englishCounted.length >= 8, "the screens' counted sentences are in the table");
  for (const [path, entry] of [...englishCounted, ...counted(LIVE_CHROME_FR_DRAFT)]) {
    assert.deepEqual(Object.keys(entry).sort(), ["one", "other"], path);
  }
});

test("a template is filled in its places, and one that disagrees with its values throws", () => {
  assert.deepEqual(fillParts("{count} states, from {from}.", { count: 2, from: "2024-02-01" }), ["2 states, from 2024-02-01."], "text is joined into one string");
  const code = { element: "code" };
  assert.deepEqual(fillParts("{iri}, held in {languages}.", { iri: code, languages: "fra, deu" }), [code, ", held in fra, deu."], "an element stays a part of its own");
  assert.equal(fillText("{title} (short title)", { title: "Loi" }), "Loi (short title)");
  assert.throws(() => fillParts("{anchor} in {work}", { anchor: "art_15" }), /no value for \{work\}/);
  assert.throws(() => fillParts("{anchor}", { anchor: "art_15", work: "loi" }), /no place for \{work\}/);
  assert.throws(() => fillText("{iri}", { iri: code }), /not text/);
});

test("a counted sentence follows the language's plural rule, not a comparison with one", () => {
  const entry = { one: "{count} version", other: "{count} versions" };
  assert.equal(fillCounted(liveChrome("en").dossier.states, 1, { from: "a", to: "b" }), "1 state, from a to b.");
  assert.equal(fillCounted(liveChrome("en").dossier.states, 0, { from: "a", to: "b" }), "0 states, from a to b.");
  assert.equal(fillCounted(entry, 0, {}, "fr"), "0 version", "French counts zero as one");
  assert.equal(fillCounted(entry, 2, {}, "fr"), "2 versions");
  assert.equal(countedEntry(entry, 1_000_000, "fr"), "{count} versions", "a form the entry lacks (French 'many') falls back to 'other'");
});

test("the table answers only for a reviewed interface language", () => {
  assert.deepEqual(Object.keys(LIVE_CHROME), REVIEWED_CHROME_LOCALES, "a table exists exactly for each reviewed language");
  for (const locale of ["fr", "de", "lb", "pt"]) {
    assert.throws(() => liveChrome(locale), /no reviewed interface copy .* localization_unavailable/, locale);
  }
  const texts = (node) => (typeof node === "string" ? [node] : Object.values(node).flatMap(texts));
  for (const text of texts(liveChrome("en"))) assert.ok(text.trim() === text && text.length > 0, JSON.stringify(text));
});
