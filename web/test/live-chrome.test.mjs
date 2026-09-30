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
import { LIVE_CHROME, liveChrome } from "../scripts/live-chrome.mjs";
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
  assert.deepEqual(Object.keys(copy).filter((key) => key !== "form").sort(), Object.keys(PAGES).sort(), "one entry per live page, and the forms");
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

test("the table answers only for a reviewed interface language", () => {
  assert.deepEqual(Object.keys(LIVE_CHROME), REVIEWED_CHROME_LOCALES, "a table exists exactly for each reviewed language");
  for (const locale of ["fr", "de", "lb", "pt"]) {
    assert.throws(() => liveChrome(locale), /no reviewed interface copy .* localization_unavailable/, locale);
  }
  const texts = (node) => (typeof node === "string" ? [node] : Object.values(node).flatMap(texts));
  for (const text of texts(liveChrome("en"))) assert.ok(text.trim() === text && text.length > 0, JSON.stringify(text));
});
