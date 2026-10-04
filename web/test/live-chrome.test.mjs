// The live pages' interface copy comes from one table (`live-chrome.mjs`), so a language added to the
// interface is one reviewed table beside English, not copy scattered through the pages (Decision 41).
//
// Each page must render exactly its table's title, eyebrow, heading and introduction, and the table
// must answer only for a reviewed language: asking it for any other is a substitution, refused. French
// is reviewed (by Claude, an AI reviewer, under the owner's delegation of 2026-10-02): its table has the
// English table's exact shape, the review's typography, and a receipt that names its reviewer as what
// it is.

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
import { LIVE_CHROME, REFUSAL_TRANSLATIONS, countedEntry, entriesOf, fillCounted, fillParts, fillText, liveChrome } from "../scripts/live-chrome.mjs";
import { REVIEWED_CHROME_LOCALES } from "../scripts/locale-unavailable.mjs";
import { CHROME_REVIEWS, isReviewed, provenanceOf } from "../scripts/localization.mjs";

/** The one entry whose spaces are its content: what stands between the items of a list said in one line. */
const SEPARATORS = new Set(["common.listSeparator"]);

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
  assert.deepEqual(Object.keys(copy).filter((key) => !["form", "common", "shell", "card", "refusalCard", "coverageAnswer"].includes(key)).sort(), Object.keys(PAGES).sort(), "one entry per live page, the forms, the page shell, the evaluation and refusal cards, the coverage answer, and what the screens share");
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

test("the French table has the English table's exact shape, and reaches the pages through the chrome table alone", async () => {
  const { LIVE_CHROME_FR } = await import("../scripts/live-chrome-fr.mjs");
  assert.equal(liveChrome("fr"), LIVE_CHROME_FR, "the reviewed table is the one the French pages say");
  const english = entriesOf(liveChrome("en")).map(([path]) => path);
  const french = entriesOf(LIVE_CHROME_FR).map(([path]) => path);
  assert.deepEqual(french, english, "one French entry per English entry, in order, and none for an entry the table no longer has");
  for (const [path, text] of entriesOf(LIVE_CHROME_FR)) {
    assert.ok(text.length > 0 && (SEPARATORS.has(path) || text.trim() === text), path);
  }
  const { renderChromeList } = await import("../scripts/refusal-sentences.mjs");
  assert.ok(!renderChromeList().includes("(no French)"), "the list the owner revises from has every entry's French");
  const { readdir, readFile } = await import("node:fs/promises");
  for (const directory of ["../app/", "../scripts/"]) {
    for (const name of await readdir(new URL(directory, import.meta.url))) {
      // The chrome table, and the list printed for the owner (which no page imports), read it; nothing else does, so a
      // page says French only as the chrome table answers it, for the language its bundle was built for.
      if (!/\.(mjs|jsx)$/.test(name) || ["live-chrome-fr.mjs", "live-chrome.mjs", "refusal-sentences.mjs"].includes(name)) continue;
      // An import of the module, not a mention of it: a comment may name the French table's file.
      assert.doesNotMatch(await readFile(new URL(`${directory}${name}`, import.meta.url), "utf8"), /(?:from\s+|import\s*\(\s*)["'][^"']*live-chrome-fr(?:\.mjs)?["']/, `${name} imports the French table around the chrome table`);
    }
  }
});

test("each French template has its English template's placeholders, and each counted entry both forms", async () => {
  const french = new Map(entriesOf(liveChrome("fr")));
  const placeholders = (text) => [...text.matchAll(/\{([A-Za-z]+)\}/g)].map((match) => match[1]).sort();
  for (const [path, english] of entriesOf(liveChrome("en"))) {
    assert.deepEqual(placeholders(french.get(path)), placeholders(english), `${path}: the French says what the English says`);
  }
  // A counted entry is an object whose keys are plural forms; each language needs "one" and "other".
  const counted = (table, path = "") => Object.entries(table).flatMap(([key, value]) => {
    if (typeof value === "string") return [];
    if (Object.keys(value).includes("one")) return [[`${path}${key}`, value]];
    return counted(value, `${path}${key}.`);
  });
  const englishCounted = counted(liveChrome("en"));
  assert.ok(englishCounted.length >= 8, "the screens' counted sentences are in the table");
  for (const [path, entry] of [...englishCounted, ...counted(liveChrome("fr"))]) {
    assert.deepEqual(Object.keys(entry).sort(), ["one", "other"], path);
  }
});

test("the French keeps the review's typography: a no-break space before : ; ! ? % and », after «, and the typographic apostrophe", () => {
  const french = [
    ...entriesOf(liveChrome("fr")),
    ...entriesOf(REFUSAL_TRANSLATIONS.fr).map(([path, text]) => [`refusals.${path}`, text]),
  ];
  assert.ok(french.length > 350, "the table and the refusal sentences are both read");
  for (const [path, text] of french) {
    // The no-break space is required, not only an ordinary space refused: "acte:" or "«{query}»" fails too (review of #912).
    assert.doesNotMatch(text, /(?<![  ])[:;!?%»]/, `${path}: a high sign without a no-break space before it`);
    assert.doesNotMatch(text, /«(?![  ])/, `${path}: « without a no-break space after it`);
    assert.doesNotMatch(text, /'/, `${path}: a straight apostrophe`);
    assert.doesNotMatch(text, / {2}/, `${path}: a double space`);
  }
  assert.equal(liveChrome("fr").common.listSeparator, " ; ", "French sets a no-break space before the semicolon between a list's items");
  assert.equal(liveChrome("fr").dossier.titleGroup, "{language} : {titles}", "and before a label's colon");
  assert.equal(liveChrome("en").common.listSeparator, "; ");
});

test("the French review's receipt names its reviewer as what it is: an AI reviewer, under the owner's delegation, and no person", () => {
  assert.deepEqual(Object.keys(CHROME_REVIEWS), Object.keys(LIVE_CHROME).filter((locale) => locale !== "en"), "every table but the English source carries a receipt, and every receipt a table");
  const receipt = CHROME_REVIEWS.fr;
  assert.equal(receipt.reviewed_by, "Claude (AI reviewer), under the owner's delegation of 2026-10-02");
  assert.equal(receipt.reviewed_on, "2026-10-03");
  assert.ok(isReviewed(receipt), "the receipt is one the localization rules accept: a reviewer and a real date");
  const provenance = provenanceOf({ text: liveChrome("fr").search.heading, ...receipt });
  assert.equal(provenance.kind, "review", "served as a review, not as a human one");
  assert.equal(provenance.reviewed_by, receipt.reviewed_by);
  assert.doesNotMatch(JSON.stringify(provenance), /human/i);
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

test("every code a reader admits has a label in the table, so a page never prints a code or nothing", async () => {
  const { ARTICLE_STATUSES } = await import("../scripts/compare-answer.mjs");
  const { RADAR_REASONS } = await import("../scripts/radar-answer.mjs");
  const copy = liveChrome("en");
  assert.deepEqual(Object.keys(copy.compare.status).sort(), [...ARTICLE_STATUSES].sort(), "a compared article's status");
  assert.deepEqual(Object.keys(copy.compare.bound).sort(), ["from", "to"], "the bound a language is not compared on");
  assert.deepEqual(Object.keys(copy.radar.reason).sort(), [...RADAR_REASONS].sort(), "why a radar row is not compared");
  const { EXPORT_FORMATS } = await import("../scripts/live-export.mjs");
  assert.deepEqual(Object.keys(copy.export.save).sort(), EXPORT_FORMATS.map((format) => format.id).sort(), "each format's save button");
});

test("the table answers only for a reviewed interface language", () => {
  assert.deepEqual(Object.keys(LIVE_CHROME), [...REVIEWED_CHROME_LOCALES], "a table exists exactly for each reviewed language");
  assert.deepEqual(Object.keys(REFUSAL_TRANSLATIONS), REVIEWED_CHROME_LOCALES.filter((locale) => locale !== "en"), "and each says the refusal sentences in its own words");
  for (const locale of ["de", "lb", "pt"]) {
    assert.throws(() => liveChrome(locale), /no reviewed interface copy .* localization_unavailable/, locale);
  }
  for (const [path, text] of entriesOf(liveChrome("en"))) {
    assert.ok(text.length > 0 && (SEPARATORS.has(path) || text.trim() === text), `${path}: ${JSON.stringify(text)}`);
  }
});
