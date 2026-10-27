// No count is a literal in what a reader is shown (the specification of record's R11; the plan's items 4.1 and 6.3): each
// count is a served field, shown "as of the index built on {date}". The pack's figures describe V2 (the record's "V2 facts
// are context, not requirements": 1,402 LU works, 1,250 EU works, ~24,579 and 23,370, 24,622, "today = 2026-08-27",
// "observation history begins August 2026"), and none may appear in copy as a literal.
//
// Before this test, the figures were refused only where a count is a value (the no-hit card's population, which must be
// counted with its date, `no-hit-card.test.mjs`); nothing held the interface's own words. This test holds:
// - every user-facing string the live pages are made of (both interface tables, the French refusal sentences, every
//   refusal sentence a screen says) and every live page as rendered, to carry none of the pack's figures, in any spelling
//   (with or without a thousands separator, comma, space or no-break space);
// - the tables to carry a number only where the allowlist below names it, each a citation of law or a statistical label,
//   so a count typed into copy fails here first;
// - the home page's coverage line to say exactly the counts the coverage answer serves, and nothing else.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  LIVE_CONTRACT, renderLiveComparePage, renderLiveCoveragePage, renderLiveDossierPage, renderLiveExportPage, renderLiveHistoryPage,
  renderLiveHomePage, renderLiveMaintenancePage, renderLiveNotFoundPage, renderLiveRadarPage, renderLiveReadingPage, renderLiveSearchPage,
  SearchAnswerView, renderLiveLegalPage, renderLiveReferencePage,
} from "../.react-build/app.mjs";
import { readDevelopersContracts } from "../scripts/developers-contracts.mjs";
import { LIVE_CHROME, REFUSAL_TRANSLATIONS, entriesOf } from "../scripts/live-chrome.mjs";
import { servedRefusalSentences } from "../scripts/refusal-sentences.mjs";
import { homeFacts } from "../scripts/live-home.mjs";
import { builtOnOf, searchOutcome } from "../scripts/live-search.mjs";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));

/** The pack's figures and dates (the specification of record, "V2 facts are context, not requirements"). */
export const PACK_FIGURES = Object.freeze(["1402", "1250", "24579", "23370", "24622", "1252", "2026-08-27", "54cceec"]);
const PACK_WORDS = /\b(?:August|ao[uû]t)\s+2026\b/i;

/** A text with every thousands separator between digits taken out: "1,402", "24 579" and "24 579" are 1402 and 24579. */
const ungrouped = (text) => text.replace(/(\d)[,   ](?=\d{3}(?!\d))/g, "$1");

/** The pack's figures a text says, as figures (never a digit run inside a longer one, a digest or an identifier). */
export function packFiguresIn(text) {
  const plain = ungrouped(text);
  const found = PACK_FIGURES.filter((figure) => new RegExp(`(?<![0-9A-Za-z-])${figure}(?![0-9A-Za-z])`).test(plain));
  return PACK_WORDS.test(text) ? [...found, plain.match(PACK_WORDS)[0]] : found;
}

/**
 * The numbers the interface tables may say, by entry: citations of law (a statute's date and article, its entry into
 * force) and statistical labels (a confidence level), and an article id's placeholder. Nothing here counts anything.
 */
const NUMBERS_ALLOWED = Object.freeze({
  "card.columns.wilson": ["95"],
  "card.columns.ruleOfThree": ["95"],
  "common.asPublished.authenticity.electronic_journal_officiel": ["1", "2017", "23", "2016", "5"],
  "common.asPublished.authenticity.printed_memorial": ["1", "2017"],
  "form.articleIdPlaceholder": ["15"],
  // The citation generator's words (`citation.mjs`): the EU's authenticity statements cite their regulation (and its article,
  // or the day from which the electronic edition alone is authentic), and the evidence form names its digest's algorithm.
  "citation.europe.authenticity.electronic_edition": ["216", "2013", "1", "2"],
  "citation.europe.authenticity.edition_by_publication_date": ["1", "2013", "216", "2013"],
  "citation.evidence.textSha256": ["256"],
  "citation.evidence.noText": ["256"],
  // The legal pages (the plan's item 7.1): the licence's version, the EU's reuse decision and the accessibility guidelines'
  // version. (The GDPR's articles and the CNPD's postal address went with the owner's decision of 2026-10-19.)
  "legalNotice.sections.sources.luxembourg": ["4", "0", "4", "0"],
  "legalNotice.sections.sources.europe": ["2011", "833"],
  "terms.sections.texts.licences": ["4", "0", "4", "0", "2011", "833"],
  "accessibility.sections.status.text": ["2", "2"],
  // The single-channel disclosure (L5; LU-DATA-DESIGN v2 §1.2): the licence's version, CC BY 4.0.
  "reading.disclosure.lu_rights_publisher_catalogue_only_file_silent": ["4", "0"],
  "reading.disclosure.lu_rights_publisher_catalogue_only_own_block_empty": ["4", "0"],
  "reading.disclosure.lu_rights_publisher_catalogue_only_file_names_other_identity": ["4", "0"],
});

const TABLES = Object.freeze([
  ["the English table", entriesOf(LIVE_CHROME.en)],
  ["the French table", entriesOf(LIVE_CHROME.fr)],
  ["the French refusal sentences", entriesOf(REFUSAL_TRANSLATIONS.fr)],
  ["the refusal sentences the screens say", servedRefusalSentences().map((row) => [row.uses.map((use) => `${use.screen}.${use.code}`).join(", "), row.sentence])],
]);

test("the scan finds a pack figure in any spelling, and a number that is a figure only", () => {
  for (const text of ["1,402 works", "1402 works", "1 402 works", "~24,579 acts", "24 579 actes", "23 370", "today = 2026-08-27", "Observation history begins August 2026", "depuis août 2026"]) {
    assert.ok(packFiguresIn(text).length > 0, text);
  }
  for (const text of ["14025 rows", "art_1402x", "sha256 a1402b", "/lu-legilux/loi-1402", "2026-08-28", "{count} acts"]) {
    assert.deepEqual(packFiguresIn(text), [], text);
  }
});

test("no user-facing string of the live pages carries a pack figure (R11)", () => {
  for (const [where, entries] of TABLES) {
    assert.ok(entries.length > 10, `${where} is read`);
    for (const [path, text] of entries) assert.deepEqual(packFiguresIn(text), [], `${where}, ${path}: ${text}`);
  }
});

test("the interface tables say a number only where the allowlist names it: a citation of law or a statistical label, never a count", () => {
  for (const table of [LIVE_CHROME.en, LIVE_CHROME.fr]) {
    const said = Object.fromEntries(entriesOf(table)
      .map(([path, text]) => [path, text.replace(/\{[A-Za-z]+\}/g, "").match(/\d+/g)])
      .filter(([, numbers]) => numbers !== null));
    assert.deepEqual(said, NUMBERS_ALLOWED, "a number in copy is a count only as a served field, filled in its template's placeholder (R11)");
  }
  for (const [where, entries] of TABLES.slice(2)) {
    for (const [path, text] of entries) assert.doesNotMatch(text.replace(/\{[A-Za-z]+\}/g, ""), /\d/, `${where}, ${path}: ${text}`);
  }
});

test("no live page, as built or rendered from the census answers, says a pack figure", async () => {
  const developers = await readDevelopersContracts();
  const coverage = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
  const reading = census.envelopes.find((entry) => entry.operation === "evidence_bundle" && entry.envelope.result !== null).envelope;
  const pages = {
    "home, as built": renderLiveHomePage(),
    "home, rendered": renderLiveHomePage({ facts: homeFacts(coverage, LIVE_CONTRACT) }),
    search: renderLiveSearchPage(),
    coverage: renderLiveCoveragePage(),
    dossier: renderLiveDossierPage(),
    reading: renderLiveReadingPage(),
    "reading, rendered": renderLiveReadingPage({ initial: { envelope: reading, path: `/en${reading.result.value.states[0].permalink}` } }),
    history: renderLiveHistoryPage(),
    compare: renderLiveComparePage(),
    radar: renderLiveRadarPage(),
    export: renderLiveExportPage(),
    "404": renderLiveNotFoundPage(),
    maintenance: renderLiveMaintenancePage(),
    ...Object.fromEntries(["legalNotice", "privacy", "terms", "accessibility", "cookies"].map((key) => [key, renderLiveLegalPage(key)])),
    // The three reference pages, the Developers page generated from the contracts, whose counts it says as served fields.
    ...Object.fromEntries(["help", "guide"].map((key) => [key, renderLiveReferencePage(key)])),
    developers: renderLiveReferencePage("developers", { developers }),
  };
  for (const [name, html] of Object.entries(pages)) {
    // What a reader is shown: the text and the human-read attributes, without the scripts and the head's machine links.
    const body = html.slice(html.indexOf("<body")).replace(/<script[\s\S]*?<\/script>/g, " ");
    const said = [...body.matchAll(/>([^<]+)</g), ...body.matchAll(/\s(?:aria-label|title|alt|placeholder)="([^"]*)"/g)].map((match) => match[1]).join(" | ");
    assert.deepEqual(packFiguresIn(said), [], name);
  }
});

/**
 * The coverage answer's text yield moved with its totals, as the producer holds the build report's yield to the mount
 * (its Luxembourg articles are the index's): served articles where there are any, and none where the mount serves none,
 * its one file then counted as an admitted file with no article.
 */
function yieldServing(value, articles) {
  const publisher = value.text_yield.publishers[0];
  const row = publisher.classes[0];
  const served = articles > 0;
  for (const node of [publisher, row]) {
    node.articles = articles;
    if (!served) Object.assign(node, { files_with_text: 0, versions_with_text: 0, works_with_text: 0 });
  }
  if (!served) row.files_parse_failed = [{ reason: "akn_unsupported_content_shape", count: row.files }];
}

/** An answer given long after any build the census records: its index is older than today, so today is said beside it. */
const LATER = "2026-12-01T10:00:00.0000000Z";

test("the home page's coverage line says the counts the coverage answer serves, and no other number than its build's date and today's", () => {
  const base = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;
  // Counts no table holds, and the pack's own figures served as counts: a served figure is shown, as served; and the same
  // answered once the index is older than today (R13; the plan's item 3.4), when today's date is said beside the build's.
  for (const [works, states, articles, later] of [[7919, 7927, 7933], [1402, 24579, 23370], [0, 0, 0], [7919, 7927, 7933, true], [1402, 24579, 23370, true]]) {
    const envelope = structuredClone(base);
    if (later) envelope.context.freshness.observed_at = LATER;
    const value = envelope.result.value;
    value.totals = { ...value.totals, works, states, articles };
    yieldServing(value, articles);
    value.languages = value.languages.map((row) => ({ ...row, works, states, articles, articles_with_searchable_text: Math.min(row.articles_with_searchable_text, articles), searchable_text_held: Math.min(row.articles_with_searchable_text, articles) > 0 }));
    value.capability_cells = articles === 0 ? [] : value.capability_cells;
    const facts = homeFacts(envelope, LIVE_CONTRACT);
    assert.equal(facts.coverage.state, "counted", `${works}, ${states}, ${articles}`);
    const html = renderLiveHomePage({ facts });
    const line = /data-coverage-line="counted"[^>]*>([^<]+)</.exec(html)?.[1];
    assert.ok(line !== undefined, "the line");
    assert.deepEqual(line.match(/\d+(?:-\d+)*/g), [String(works), String(states), String(articles), facts.coverage.builtOn, ...(later ? ["2026-12-01"] : [])], line);
  }
});

test("a search's population line says the counts the search answer serves, and no other number than its index's build date and today's (the plan's items 4.3 and 3.4)", () => {
  const base = census.envelopes.find((entry) => entry.operation === "search" && entry.scenario.startsWith("the phrase as the site asks it")).envelope;
  // Counts no table holds, the pack's own figures served as counts, and none: a served figure is shown, as served; and the
  // same answered once the index is older than today, when today's date is said beside the build's (R13).
  for (const [works, states, articles, later] of [[7919, 7927, 7933], [1402, 24579, 23370], [0, 0, 0], [7919, 7927, 7933, true], [0, 0, 0, true]]) {
    const envelope = structuredClone(base);
    if (later) envelope.context.freshness.observed_at = LATER;
    const value = envelope.result.value;
    const hits = value.hits.length;
    value.searched = { ...value.searched, works: Math.max(works, 1), states: Math.max(states, 1), articles_with_searchable_text: Math.max(articles, hits) };
    const outcome = searchOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const html = renderToStaticMarkup(h(SearchAnswerView, { outcome }));
    const line = /<p data-searched="\d+"[^>]*>([^<]+)</.exec(html)?.[1];
    assert.ok(line !== undefined, "the line");
    const served = value.searched;
    assert.deepEqual(line.match(/\d+(?:-\d+)*/g), [String(served.works), String(served.states), String(served.articles_with_searchable_text), builtOnOf(served.index_built_at), ...(later ? ["2026-12-01"] : [])], line);
  }
});
