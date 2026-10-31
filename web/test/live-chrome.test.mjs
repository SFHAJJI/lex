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
  renderLiveHomePage,
  renderLiveMaintenancePage,
  renderLiveNotFoundPage,
  renderLiveRadarPage,
  renderLiveReadingPage,
  renderLiveSearchPage,
  renderLiveLegalNoticePage,
  renderLivePrivacyPage,
  renderLiveTermsPage,
  renderLiveAccessibilityPage,
  renderLiveCookiesPage,
  renderLiveDevelopersPage,
  renderLiveGuidePage,
  renderLiveHelpPage,
} from "../.react-build/app.mjs";
import { readDevelopersContracts } from "../scripts/developers-contracts.mjs";
import { LIVE_CHROME, REFUSAL_TRANSLATIONS, countedEntry, entriesOf, fillCounted, fillParts, fillText, liveChrome } from "../scripts/live-chrome.mjs";
import { REVIEWED_CHROME_LOCALES } from "../scripts/locale-unavailable.mjs";
import { CHROME_REVIEWS, isReviewed, provenanceOf } from "../scripts/localization.mjs";

/** The entries whose spaces are their content: what stands between the items of a list, or of a list of codes, said in one line. */
const SEPARATORS = new Set(["common.listSeparator", "developers.codeSeparator"]);

const PAGES = {
  home: renderLiveHomePage,
  coverage: renderLiveCoveragePage,
  search: renderLiveSearchPage,
  dossier: renderLiveDossierPage,
  reading: renderLiveReadingPage,
  history: renderLiveHistoryPage,
  compare: renderLiveComparePage,
  radar: renderLiveRadarPage,
  export: renderLiveExportPage,
  // The 404 page and the maintenance page (R60) carry the site's header and footer like every page.
  notFound: renderLiveNotFoundPage,
  maintenance: renderLiveMaintenancePage,
  // The five legal pages every footer links (the plan's item 7.1).
  legalNotice: renderLiveLegalNoticePage,
  privacy: renderLivePrivacyPage,
  terms: renderLiveTermsPage,
  accessibility: renderLiveAccessibilityPage,
  cookies: renderLiveCookiesPage,
  // The three reference pages every footer links (Get help, How to read Lex, Developers: `reference-pages.mjs`).
  help: renderLiveHelpPage,
  guide: renderLiveGuidePage,
  developers: () => renderLiveDevelopersPage({ developers: DEVELOPERS }),
};
const DEVELOPERS = await readDevelopersContracts();

/** The legal pages and the reference pages, which ask nothing and carry no form. */
const LEGAL = new Set(["legalNotice", "privacy", "terms", "accessibility", "cookies", "help", "guide", "developers"]);

// React escapes an apostrophe in text; the table holds the text itself.
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("'", "&#x27;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");

test("every live page renders its chrome from the table", () => {
  const copy = liveChrome("en");
  assert.deepEqual(Object.keys(copy).filter((key) => !["form", "common", "shell", "site", "breadcrumbs", "freshness", "card", "refusalCard", "coverageAnswer", "failure", "citation", "legal", "reference"].includes(key)).sort(), Object.keys(PAGES).sort(), "one entry per live page, the forms, the page shell, the site around it, its breadcrumbs and its freshness strip, the evaluation and refusal cards, the coverage answer, the failure panel, what the screens share, the citation generator's words, what the legal pages share and what the reference pages share");
  for (const [key, render] of Object.entries(PAGES)) {
    const html = render();
    const entry = copy[key];
    assert.ok(html.includes(`<title>${escaped(entry.title)} - Lex</title>`), `${key}: title`);
    // The site around the screen: its name, its menu, and the statements every page carries (no development banner).
    for (const text of [copy.site.brand, copy.site.tagline, copy.site.skip, copy.site.search, copy.site.reading, copy.site.dossier, copy.site.history, copy.site.compare, copy.site.radar, copy.site.export, copy.site.coverage, copy.site.notAdvice, copy.site.authenticity, copy.site.sources]) {
      assert.ok(html.includes(escaped(text)), `${key}: the site says ${JSON.stringify(text.slice(0, 40))}`);
    }
    assert.ok(!html.includes("development build"), `${key}: no development banner`);
    // Every page as built carries the freshness strip in its footer (R87, R23), and no staleness banner: the as-built line on
    // a page that asks the server, and on one that never does (the maintenance page and the legal pages) that it carries no
    // answer, never the as-built line's "once it has an answer" (the review of #990).
    const quiet = key === "maintenance" || LEGAL.has(key);
    assert.ok(html.includes(`<p>${escaped(quiet ? copy.freshness.noAnswer : copy.freshness.asBuilt)}</p>`), `${key}: the freshness strip as built`);
    assert.ok(!html.includes(escaped(quiet ? copy.freshness.asBuilt : copy.freshness.noAnswer)), `${key}: only that strip`);
    assert.ok(!html.includes("staleness-banner"), `${key}: no staleness banner as built`);
    assert.ok(html.includes(`<p class="eyebrow">${escaped(entry.eyebrow)}</p>`), `${key}: eyebrow`);
    assert.ok(html.includes(`<h1>${escaped(entry.heading)}</h1>`), `${key}: heading`);
    assert.ok(html.includes(`<p>${escaped(entry.intro)}</p>`), `${key}: introduction`);
  }
});

test("every form's labels and button come from the table", () => {
  const { form } = liveChrome("en");
  for (const [key, render] of Object.entries(PAGES)) {
    // Trust and Coverage asks with no form, and the maintenance page and the legal pages ask nothing.
    if (key === "coverage" || key === "maintenance" || LEGAL.has(key)) continue;
    // The home page is the search screen under the site's introduction, and the 404 page offers it: its form is search's.
    const formKey = key === "home" || key === "notFound" ? "search" : key;
    // A page rendered for hydration marks text boundaries with empty comments; they are not text.
    const html = render().replaceAll("<!-- -->", "");
    assert.ok(html.includes(`<button type="submit">${escaped(form.submit[formKey])}</button>`), `${key}: the submit button`);
    assert.ok(html.includes(`<option value="">${escaped(form.anyLanguage)}</option>`) || html.includes(`<option value="" selected="">${escaped(form.anyLanguage)}</option>`) || formKey === "search", `${key}: the blank language option`);
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
  const { EUROPE_FORCE_DATE_ROLES, EUROPE_FORCE_DATE_NAMED_ROLES } = await import("../scripts/europe-force-dates.mjs");
  assert.deepEqual(Object.keys(copy.common.europeForceDates.roles), [...EUROPE_FORCE_DATE_ROLES], "an EU force date's role, in its order");
  assert.deepEqual(Object.keys(copy.common.europeForceDates.roleNames), [...EUROPE_FORCE_DATE_NAMED_ROLES], "a role not held");
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

test("the L5 single-channel disclosures are final, in the review's words: the file's licence metadata, one licence row of the publisher's catalogue, the page and the day this site read it (the owner's standing yes of 2026-10-27; the addendum of 2026-10-28)", () => {
  // Byte for byte as the review's addendum of 2026-10-28 gave them (a no-break space before the French colon).
  const finals = {
    en: {
      lu_rights_publisher_catalogue_only_file_silent: "This file's metadata states no licence. The text of this file is served on the basis of the publisher's catalogue alone: a licence row in that catalogue declares CC BY 4.0 for this file, on a page that this site read on {date}.",
      lu_rights_publisher_catalogue_only_own_block_empty: "This file's own metadata names it but does not state its licence. The text of this file is served on the basis of the publisher's catalogue alone: a licence row in that catalogue declares CC BY 4.0 for this file, on a page that this site read on {date}.",
      lu_rights_publisher_catalogue_only_file_names_other_identity: "This file's licence statement names another file, not this file. The text of this file is served on the basis of the publisher's catalogue alone: a licence row in that catalogue declares CC BY 4.0 for this file, on a page that this site read on {date}.",
    },
    fr: {
      lu_rights_publisher_catalogue_only_file_silent: "Les métadonnées de ce fichier n’indiquent aucune licence. Le texte de ce fichier est communiqué sur la seule base du catalogue de l’éditeur : une ligne de licence de ce catalogue déclare la licence CC BY 4.0 pour ce fichier, sur une page que ce site a lue le {date}.",
      lu_rights_publisher_catalogue_only_own_block_empty: "Les métadonnées propres de ce fichier le désignent sans indiquer sa licence. Le texte de ce fichier est communiqué sur la seule base du catalogue de l’éditeur : une ligne de licence de ce catalogue déclare la licence CC BY 4.0 pour ce fichier, sur une page que ce site a lue le {date}.",
      lu_rights_publisher_catalogue_only_file_names_other_identity: "La mention de licence de ce fichier désigne un autre fichier, et non ce fichier. Le texte de ce fichier est communiqué sur la seule base du catalogue de l’éditeur : une ligne de licence de ce catalogue déclare la licence CC BY 4.0 pour ce fichier, sur une page que ce site a lue le {date}.",
    },
  };
  for (const language of ["en", "fr"]) {
    const disclosure = LIVE_CHROME[language].reading.disclosure;
    assert.deepEqual({ ...disclosure }, finals[language], `${language}: the three final sentences, and no other`);
    for (const [code, sentence] of Object.entries(disclosure)) {
      assert.equal(sentence.split("{date}").length, 2, `${language} ${code}: the day the page was read, once`);
      assert.match(sentence, language === "en" ? /a licence row in that catalogue declares CC BY 4\.0 for this file, on a page that this site read on \{date\}\.$/
        : /une ligne de licence de ce catalogue déclare la licence CC BY 4\.0 pour ce fichier, sur une page que ce site a lue le \{date\}\.$/, `${language} ${code}`);
    }
  }
  // The drafts' words the review withdrew are said nowhere: "served on" a party, "sur la seule foi" beside "Seuls font foi",
  // a page "de celui-ci" (the file's), and "another publication", which the code does not check.
  const english = JSON.stringify(LIVE_CHROME.en.reading.disclosure);
  const french = JSON.stringify(LIVE_CHROME.fr.reading.disclosure);
  for (const withdrawn of ["It is served on the publisher's catalogue", "another publication", "as observed on"]) assert.ok(!english.includes(withdrawn), withdrawn);
  for (const withdrawn of ["sur la seule foi", "page de celui-ci", "Il est communiqué sur", "une autre publication", "tel qu’observé"]) assert.ok(!french.includes(withdrawn), withdrawn);
});
