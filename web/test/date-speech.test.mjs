// The two date kinds are never merged, in text or in speech, per publisher (the launch contract's
// promise "Luxembourg applicability dates and EU wording-state dates are never merged", whose
// evidence is a timeline speech test per publisher).
//
// A Luxembourg date is the publisher's applicability date of a state ("applies from", "version of");
// an EU date is the date the publisher's Formex package gives the one wording held ("wording of",
// "Wording date"). The words a page says about a date are held apart three ways:
//
// - what the platform sends: no EU answer in either census carries a Luxembourg state's date fields,
//   and no Luxembourg answer an EU wording's;
// - what the interface table says: the entries the EU views use carry none of the Luxembourg date
//   words, in English or in the reviewed French, and no other entry carries the EU ones; nor does an
//   EU refusal sentence, in either language;
// - what each screen says, and what a screen reader hears, for each census answer: rendered through
//   its screen, with the answer's own values taken out, the text and the human-read attributes of a
//   Luxembourg answer use no EU date words, and an EU answer's no Luxembourg ones; and the same of the
//   export composer with every article pinned and the export composed, for the census's Luxembourg
//   bundle and the hand-built EU one the web tests share (`scripts/europe-bundle-sample.mjs`).
//
// The platform's own sentences are data here, as in the chrome scan: an EU answer says, in its own
// words, that its date is never merged with a Luxembourg applicability date, and that sentence
// naming the other kind is the promise kept, not broken.
//
// And per date role (slice S7b; R21: "entry into force and application come only from typed publisher dates, spoken as
// separate values"): on lex-v3-luxembourg-index/9 an act its file does not date is dated by its entry into force, and no
// screen says that date as an application, in English or in French. Every census Luxembourg answer is said again with its
// first state so dated, as index 9 dates an act as published (only an act as published is, and it is a work's first state),
// and no application word stands right before that date in what the screen says or a screen reader hears.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import test from "node:test";
import { build } from "esbuild";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  CompareAnswerView,
  CoverageAnswerView,
  DossierAnswerView,
  ExportAnswerView,
  ExportPanel,
  HistoryAnswerView,
  RadarAnswerView,
  ReadingAnswerView,
  SearchAnswerView,
} from "../.react-build/app.mjs";
import { europeEnvelope } from "../scripts/europe-bundle-sample.mjs";
import { LIVE_CHROME, REFUSAL_TRANSLATIONS, entriesOf } from "../scripts/live-chrome.mjs";
import { exportState, pinKey } from "../scripts/live-export.mjs";
import { searchOutcome } from "../scripts/live-search.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { LIVE_READING_EUROPE_REFUSAL_SENTENCES, readingOutcome } from "../scripts/live-reading.mjs";
import { historyOutcome } from "../scripts/live-history.mjs";
import { compareOutcome } from "../scripts/live-compare.mjs";
import { radarOutcome } from "../scripts/live-radar.mjs";
import { coverageOutcome } from "../scripts/live-coverage.mjs";

const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));

/** The Luxembourg date words: a state's applicability, its version, in English and in French. */
const LUXEMBOURG_DATE_WORDS = /\b(?:appl(?:y|ies|ied|ying|icability)|versions?|states?)\b|s[’']appliqu|applicab/i;

/**
 * The EU date words: a wording and its date, in English and in French. "Wording" and "libellé" alone
 * are not among them: a Luxembourg article's wording changes from state to state ("wording changed",
 * "libellé modifié"); only a wording that is dated is the EU's.
 */
const EUROPE_DATE_WORDS = /\bwording (?:of|date)\b|\bdated (?:\{|\d{4}-)|libellé du (?:\{|\d{4}-)|date du libellé|daté du (?:\{|\d{4}-)/i;

/** The fields that carry each kind of date, or name what it dates. */
const LUXEMBOURG_DATE_FIELDS = new Set(["applicability_date", "next_applicability_date", "latest_applicability_date", "history_begins", "state_sha256"]);
// Not `wording_sha256` alone: a Luxembourg article history names each article wording's digest.
const EUROPE_DATE_FIELDS = new Set(["wording_date", "wording_dates", "pinned_wording"]);

/**
 * The interface entries the EU views say, which name the EU date and only it: the views' own, and the
 * EU-only entries the review of the French found outside the first set (the reading's counts, the
 * refusal card's declared nulls and its offered wordings), and the EU export's.
 */
const EUROPE_ENTRIES = new Set([
  "search.euWording", "search.euHit", "search.notHeldHeading", "dossier.euExpressions.one", "dossier.euExpressions.other",
  "dossier.wordingDate", "reading.europeWordingHeading", "reading.europeOriginalHeading", "reading.europeConsolidatedHeading",
  "reading.europeHoldsUntil", "reading.europeLatest", "reading.europeCounts.one", "reading.europeCounts.other",
  "export.europeRights", "export.europeItem", "export.europeExcluded",
  "refusalCard.europeNullSentences.nearest_earlier", "refusalCard.europeNullSentences.nearest_later",
  "refusalCard.europeCandidate", "refusalCard.europeCandidateWithdrawalNotStated", "refusalCard.europePublished",
  "refusalCard.europeNotes.ambiguous_version",
  "reading.europeSameDateWorks.one", "reading.europeSameDateWorks.other", "reading.europeUnplaced.one", "reading.europeUnplaced.other",
  "common.europeAnnexes.one", "common.europeAnnexes.other",
  // The Treaties' items left out, by the wording they are published with (the review of #996, F2).
  "common.europeTreatyParts.one", "common.europeTreatyParts.other",
  "reading.europePinReplaced", "reading.europePinNotHeld", "reading.europeWholeAct", "reading.europeNewerWording",
  "reading.europeLaterWordingNotHeld",
  // A page past an EU wording's last (the review of #978, F2).
  "reading.europePageNotHeld.one", "reading.europePageNotHeld.other",
  // An EU reading's breadcrumb for its wording, by its own date (R48, `breadcrumbs.mjs`), and the law the EU publishes.
  "breadcrumbs.europeOriginal", "breadcrumbs.europeConsolidated", "breadcrumbs.publishers.eu-eurlex",
  // The citation generator's EU words (`citation.mjs`): an EU act's name and the date it is cited at, the wording's line of
  // the evidence form, the Official Journal reference no index holds, and the EU's authenticity statement.
  "citation.europe.article", "citation.europe.acts.R", "citation.europe.acts.L", "citation.europe.acts.D",
  "citation.europe.domains.EU", "citation.europe.domains.EC", "citation.europe.domains.EEC", "citation.europe.numbered",
  "citation.europe.numberedNoDomain", "citation.europe.yearFirst", "citation.europe.suffixed", "citation.europe.plain",
  "citation.europe.version", "citation.europe.authenticity.electronic_edition",
  "citation.europe.authenticity.edition_by_publication_date", "citation.evidence.originalWording",
  "citation.evidence.consolidatedWording", "citation.evidence.officialJournalNotHeld",
  // The EU act's dates as the publisher records them (the plan's item 2.1; `europe-force-dates.mjs`): EU pages only.
  "common.europeForceDates.heading", "common.europeForceDates.roles.entry_into_force", "common.europeForceDates.roles.application",
  "common.europeForceDates.roles.end_of_validity", "common.europeForceDates.roles.deadline", "common.europeForceDates.roles.signature",
  "common.europeForceDates.roles.not_pinned", "common.europeForceDates.notPinnedNoCode", "common.europeForceDates.openEnd",
  "common.europeForceDates.qualifier", "common.europeForceDates.roleNames.entry_into_force", "common.europeForceDates.roleNames.application",
  "common.europeForceDates.roleNames.end_of_validity", "common.europeForceDates.roleNames.deadline", "common.europeForceDates.notHeld",
  "common.europeForceDates.conflict", "common.europeForceDates.notWording",
]);

/**
 * The entries the reading's lead says on both publishers' pages (`PinnedReading.jsx`): they name the version through its own
 * heading, so they say no date word of either publisher themselves (the French review of 2026-10-16 asked for the check).
 */
const SHARED_LEAD_ENTRIES = new Set([
  "reading.citedTitle", "reading.citedFrom", "reading.citedWithoutText", "reading.openToday",
  // The cited article's citations, under the same labels on both publishers' pages (`Citations`), and the lines of the
  // citation generator both publishers' forms say (`citation.mjs`).
  "reading.cite", "reading.citeConventional", "reading.citeWithPermalink", "reading.citeEvidence", "reading.citeNotComposed",
  "export.usualCitation", "citation.withPermalink", "citation.date", "citation.firstOfMonth", "citation.evidence.permalink",
  "citation.evidence.textSha256", "citation.evidence.noText", "citation.evidence.consulted",
  // A long version's pages, said on both publishers' pages (the plan's item 3.7; the French review of 2026-10-18, point 5).
  "reading.contents", "reading.contentsPage", "reading.contentsPageOne", "reading.pageShown", "reading.pageShownOne",
  "reading.pagesShown", "reading.pager", "reading.previousPage", "reading.nextPage", "reading.nextPageFailed",
  "export.pinAll", "export.readingPages", "export.pagesFailed",
  // The links after a page past a version's last (the review of #978, F2).
  "reading.firstPage", "reading.lastPage",
]);

function fieldsOf(node, found = new Set()) {
  if (Array.isArray(node)) node.forEach((item) => fieldsOf(item, found));
  else if (node !== null && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      found.add(key);
      fieldsOf(value, found);
    }
  }
  return found;
}

test("no EU answer carries a Luxembourg state's date, and no Luxembourg answer an EU wording's", () => {
  const sent = [
    ...answers.sampled.map((sample) => [`answer census: ${sample.operation} / ${sample.scenario}`, sample.answer]),
    ...census.envelopes.filter((entry) => entry.envelope.result).map((entry) => [`envelope census: ${entry.operation} / ${entry.scenario}`, entry.envelope.result.value]),
  ];
  const seen = { "eu-eurlex": 0, "lu-legilux": 0 };
  for (const [label, answer] of sent) {
    const publisher = answer.publisher ?? answer.mounted?.publisher ?? null;
    if (!(publisher in seen)) continue;
    seen[publisher] += 1;
    const [forbidden, kind] = publisher === "eu-eurlex" ? [LUXEMBOURG_DATE_FIELDS, "a Luxembourg state's"] : [EUROPE_DATE_FIELDS, "an EU wording's"];
    const carried = [...fieldsOf(answer)].filter((field) => forbidden.has(field));
    assert.deepEqual(carried, [], `${label} (${publisher}) carries ${kind} date fields`);
  }
  assert.ok(seen["eu-eurlex"] >= 6 && seen["lu-legilux"] >= 10, `both publishers' answers were read: ${JSON.stringify(seen)}`);
});

test("the interface's EU entries say no Luxembourg date word, and no other entry says an EU one, in English and in French", () => {
  for (const [language, table] of [["en", LIVE_CHROME.en], ["fr", LIVE_CHROME.fr]]) {
    const entries = entriesOf(table);
    const shared = entries.filter(([path]) => SHARED_LEAD_ENTRIES.has(path));
    assert.equal(shared.length, SHARED_LEAD_ENTRIES.size, `${language}: every entry of the reading's lead is in the table`);
    for (const [path, text] of shared) {
      assert.doesNotMatch(text, LUXEMBOURG_DATE_WORDS, `${language} ${path}, said on EU pages too, says a Luxembourg date word`);
      assert.doesNotMatch(text, EUROPE_DATE_WORDS, `${language} ${path}, said on Luxembourg pages too, says an EU date word`);
    }
    const europe = entries.filter(([path]) => EUROPE_ENTRIES.has(path));
    assert.equal(europe.length, EUROPE_ENTRIES.size, `${language}: every EU entry is in the table`);
    for (const [path, text] of europe) {
      assert.doesNotMatch(text, LUXEMBOURG_DATE_WORDS, `${language} ${path} says a Luxembourg date word`);
    }
    // A page's introduction says what the page holds for each publisher, each in its own words; every
    // other entry speaks of one publisher's dates and must not borrow the EU's.
    for (const [path, text] of entries.filter(([path]) => !EUROPE_ENTRIES.has(path) && !path.endsWith(".intro"))) {
      assert.doesNotMatch(text, EUROPE_DATE_WORDS, `${language} ${path} says an EU date word`);
    }
  }
  // The refusal sentences an EU refusal says, as the English and French pages say them.
  for (const [code, english] of Object.entries(LIVE_READING_EUROPE_REFUSAL_SENTENCES)) {
    for (const said of [english, REFUSAL_TRANSLATIONS.fr.sentences[english]]) {
      assert.equal(typeof said, "string", `${code}: said in both languages`);
      assert.doesNotMatch(said, LUXEMBOURG_DATE_WORDS, `the EU refusal sentence for ${code} says a Luxembourg date word: ${said}`);
    }
  }
});

const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'", "&#39;": "'", "&nbsp;": " " };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|nbsp|#x27|#39);/g, (entity) => ENTITIES[entity]);

/** What a page says and what a screen reader hears: every text node and every human-read attribute. */
function spoken(markup) {
  const texts = [...markup.matchAll(/>([^<]+)</g)].map((match) => decode(match[1]));
  const attributes = [...markup.matchAll(/\s(?:aria-label|title|alt|placeholder)="([^"]*)"/g)].map((match) => decode(match[1]));
  return [...texts, ...attributes];
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

/** The interface's own words in a render: what is left once the answer's values are taken out (capitalised too, as a sentence starts). */
function interfaceText(markup, envelope) {
  const data = dataOf(envelope);
  return spoken(markup).map((text) => {
    let rest = text;
    for (const value of data) {
      rest = rest.split(value).join(" ");
      const capitalised = `${value.charAt(0).toUpperCase()}${value.slice(1)}`;
      if (capitalised !== value) rest = rest.split(capitalised).join(" ");
    }
    return rest;
  }).join(" | ");
}

const SCREENS = Object.freeze([
  ["search", searchOutcome, SearchAnswerView],
  ["dossier", dossierOutcome, DossierAnswerView],
  ["evidence_bundle", readingOutcome, ReadingAnswerView],
  ["evidence_bundle", readingOutcome, ExportAnswerView],
  ["article_history", historyOutcome, HistoryAnswerView],
  ["diff", compareOutcome, CompareAnswerView],
  ["changes_in_period", radarOutcome, RadarAnswerView],
  ["coverage", coverageOutcome, CoverageAnswerView],
]);

test("each screen says and speaks only its publisher's date words, for every census answer", () => {
  const spokenBy = { "eu-eurlex": [], "lu-legilux": [] };
  for (const [operation, outcomeOf, View] of SCREENS) {
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation && candidate.envelope.result)) {
      const outcome = outcomeOf({ state: "success", envelope: entry.envelope });
      assert.equal(outcome.state, "success", `${operation}: ${entry.scenario}`);
      const publisher = entry.envelope.result.value.publisher ?? entry.envelope.result.value.mounted?.publisher ?? entry.envelope.context.publisher;
      const markup = renderToStaticMarkup(h(View, { outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} }));
      const said = interfaceText(markup, entry.envelope);
      const [forbidden, kind] = publisher === "eu-eurlex" ? [LUXEMBOURG_DATE_WORDS, "a Luxembourg date word"] : [EUROPE_DATE_WORDS, "an EU date word"];
      assert.doesNotMatch(said, forbidden, `${operation} (${publisher}), ${entry.scenario}, says ${kind}: ${said.match(forbidden)?.[0]}`);
      spokenBy[publisher].push(`${operation}: ${said}`);
    }
  }
  // Each publisher's screens were read, and each says its own date words where it shows a date.
  assert.ok(spokenBy["eu-eurlex"].some((said) => said.startsWith("search:") && /wording of/.test(said)), "an EU search hit is a wording of its date");
  assert.ok(spokenBy["eu-eurlex"].some((said) => said.startsWith("dossier:") && /Wording date/.test(said)), "an EU dossier's date is a wording date");
  assert.ok(spokenBy["lu-legilux"].some((said) => said.startsWith("search:") && /applicable from/.test(said)), "a Luxembourg search hit's version is applicable from its date (publisher), R21");
  assert.ok(spokenBy["lu-legilux"].some((said) => said.startsWith("dossier:") && /Applies from/.test(said)), "a Luxembourg dossier's date is when a state applies from");
});

test("the export composer says and speaks only its publisher's date words, with every article pinned and the export composed", () => {
  // The census holds no EU bundle yet: the EU reading is the hand-built bundle the web tests share.
  const luxembourg = census.envelopes.find((entry) => entry.operation === "evidence_bundle" && entry.envelope.result).envelope;
  for (const envelope of [europeEnvelope(), luxembourg]) {
    const outcome = readingOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    const { view } = outcome;
    const europe = view.publisher === "eu-eurlex";
    const held = europe ? view.wordings.map((wording) => [wording.wordingSha256, wording]) : view.states.map((state) => [state.stateSha256, state]);
    const pins = new Set(held.flatMap(([digest, holder]) => [...holder.articles, ...holder.articlesWithoutText].map((article) => pinKey(digest, article.publisherId))));
    const panel = exportState(outcome, pins);
    assert.equal(panel.state, "composed", panel.sentence);
    const markup = renderToStaticMarkup(h(ExportAnswerView, { outcome, pins, onPin: () => {} })) + renderToStaticMarkup(h(ExportPanel, { outcome, pins, onSave: () => {} }));
    // The export's own values are data too: the watermark, and what it names an exclusion by.
    const said = interfaceText(markup, [envelope, panel.model]);
    const [forbidden, kind, own] = europe
      ? [LUXEMBOURG_DATE_WORDS, "a Luxembourg date word", /wording of/]
      : [EUROPE_DATE_WORDS, "an EU date word", /applying from/];
    assert.doesNotMatch(said, forbidden, `the ${view.publisher} export says ${kind}: ${said.match(forbidden)?.[0]}`);
    assert.match(said, own, `the ${view.publisher} export dates its articles in its own words`);
  }
});

test("the speech test fails a screen that borrows the other publisher's date words", () => {
  // The two checks, each fed what they exist to catch, so a pattern that matches nothing cannot pass.
  assert.match("Article 26 of 32016R0679, version of 2016-04-27", LUXEMBOURG_DATE_WORDS);
  assert.match("Applies from", LUXEMBOURG_DATE_WORDS);
  assert.match("la version s’applique", LUXEMBOURG_DATE_WORDS);
  assert.match("art_15 in loi-1991-08-10-n3, wording of 2024-02-01", EUROPE_DATE_WORDS);
  assert.match("Wording date", EUROPE_DATE_WORDS);
  assert.match("dated 2024-02-01", EUROPE_DATE_WORDS);
  assert.match("libellé du {date}", EUROPE_DATE_WORDS);
  assert.match("Date du libellé", EUROPE_DATE_WORDS);
  assert.doesNotMatch("libellé modifié", EUROPE_DATE_WORDS);
  // And not what each publisher rightly says: a Luxembourg wording that changed is not an EU date.
  assert.doesNotMatch("wording changed", EUROPE_DATE_WORDS);
  assert.doesNotMatch("publisher-dated states", EUROPE_DATE_WORDS);
  assert.doesNotMatch("1 expression held, shown with its original wording.", LUXEMBOURG_DATE_WORDS);

  const envelope = census.envelopes.find((entry) => entry.operation === "search" && entry.scenario.startsWith("one EU work by its CELEX")).envelope;
  const outcome = searchOutcome({ state: "success", envelope });
  const markup = renderToStaticMarkup(h(SearchAnswerView, { outcome, onNextPage: () => {} }));
  const borrowed = markup.replaceAll(", wording of ", ", version of ");
  assert.notEqual(borrowed, markup, "the EU hit row says 'wording of'");
  assert.match(interfaceText(borrowed, envelope), LUXEMBOURG_DATE_WORDS, "an EU hit called a version is caught");
});

/**
 * The words of an application said right before a date, in English and in French: what R21 never says of an entry into
 * force. "applies from" of the next state held is said of that state's own date, which is another date.
 */
// A bare "from" or "à partir du" right before the date is caught too: it says the date is one the state applies from, which
// R21 never says of an entry into force (the review of #987: the radar's row said "from {date}" with no role).
const APPLICATION_BEFORE = /(?:appl(?:y|ies|ying|icable)(?: (?:from|on|to))?|s[’']appliqu\p{L}*(?: à partir)?(?: du| au| le)?|applicables? (?:à partir )?(?:du|au|le|jusqu[’']au)|\bfrom|à partir du)\s*$/iu;

/** Where a screen says an application word right before a date: each such place, with the words before it. */
function applicationSaidOf(markup, date) {
  const plain = decode(markup.replace(/<!--.*?-->/g, "").replace(/<[^>]+>/g, " ")).replace(/\s+/g, " ");
  const found = [];
  for (let at = plain.indexOf(date); at !== -1; at = plain.indexOf(date, at + 1)) {
    const before = plain.slice(Math.max(0, at - 48), at);
    if (APPLICATION_BEFORE.test(before)) found.push(`…${before}${date}`);
  }
  return found;
}

/** The first state of a census Luxembourg answer dated by its entry into force, as index 9 dates an act as published. */
function datedByItsEntryIntoForce(operation, envelope) {
  const value = structuredClone(envelope.result.value);
  const stated = (holder) => holder !== null && typeof holder === "object" && typeof holder.applicability_date === "string" && typeof holder.state_sha256 === "string";
  const holders = operation === "search" ? value.hits
    : operation === "diff" ? value.comparisons.flatMap((comparison) => [comparison.from, comparison.to])
    : operation === "changes_in_period" ? value.changes.flatMap((change) => [change.state, change.baseline]).filter((holder) => holder !== null)
    : value.states;
  const dated = (holders ?? []).filter(stated);
  if (dated.length === 0) return null;
  const first = dated.map((holder) => holder.applicability_date).sort()[0];
  for (const holder of dated.filter((one) => one.applicability_date === first)) {
    holder.date_role = "entry_into_force";
    holder.entry_into_force = first;
    if (Object.hasOwn(holder, "state_kind")) holder.state_kind = "as_published";
  }
  // The platform's same-state note says that date by its role too (`SameVersionNote`, `AsPublishedSameTextNote`).
  if (operation === "diff") {
    for (const comparison of value.comparisons.filter((one) => one.same_state && one.from.applicability_date === first)) {
      comparison.note = comparison.note.replace(`applicable from ${first} (publisher)`, `dated by its entry into force on ${first} (publisher)`);
    }
  }
  return { envelope: { ...structuredClone(envelope), result: { ...envelope.result, value } }, date: first };
}

async function frenchViews() {
  const web = new URL("../", import.meta.url);
  const out = fileURLToPath(new URL(".react-build/app-fr-date-speech-test.mjs", web));
  await build({
    stdin: { contents: "export * from './app/index.jsx';", resolveDir: fileURLToPath(web), sourcefile: "date-speech-french-test.jsx", loader: "jsx" },
    outfile: out,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
  });
  return import(`${pathToFileURL(out).href}?${Date.now()}`);
}

test("per date role: no screen says an entry into force as an application, in English or in French, for every census answer", async () => {
  const french = await frenchViews();
  const views = { en: { SearchAnswerView, DossierAnswerView, ReadingAnswerView, ExportAnswerView, ExportPanel, HistoryAnswerView, CompareAnswerView, RadarAnswerView }, fr: french };
  const screens = [
    ["search", searchOutcome, "SearchAnswerView"],
    ["dossier", dossierOutcome, "DossierAnswerView"],
    ["evidence_bundle", readingOutcome, "ReadingAnswerView"],
    ["evidence_bundle", readingOutcome, "ExportAnswerView"],
    ["article_history", historyOutcome, "HistoryAnswerView"],
    ["diff", compareOutcome, "CompareAnswerView"],
    // The radar's rows (the review of #987).
    ["changes_in_period", radarOutcome, "RadarAnswerView"],
  ];
  let said = 0;
  const screensSaid = new Set();
  for (const [operation, outcomeOf, name] of screens) {
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation && candidate.envelope.result?.value?.publisher === "lu-legilux")) {
      const redated = datedByItsEntryIntoForce(operation, entry.envelope);
      if (redated === null) continue;
      const outcome = outcomeOf({ state: "success", envelope: redated.envelope });
      assert.equal(outcome.state, "success", `${operation} ${entry.scenario}: ${outcome.sentence}`);
      for (const [language, view] of Object.entries(views)) {
        const props = { outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} };
        let markup = renderToStaticMarkup(h(view[name], props));
        if (name === "ExportAnswerView") {
          // The export composed with every article pinned, so its item lines are said too.
          const state = outcome.view.states[0];
          const pins = new Set([...state.articles, ...state.articlesWithoutText].map((article) => pinKey(state.stateSha256, article.publisherId)));
          markup = renderToStaticMarkup(h(view.ExportAnswerView, { ...props, pins })) + renderToStaticMarkup(h(view.ExportPanel, { outcome, pins, onSave: () => {} }));
        }
        assert.ok(decode(markup).includes(redated.date), `${language} ${name} (${entry.scenario}) says the date`);
        assert.deepEqual(applicationSaidOf(markup, redated.date), [], `${language} ${name} (${entry.scenario}) says an entry into force as an application`);
        said += 1;
        screensSaid.add(`${language} ${name}`);
      }
    }
  }
  assert.ok(said >= 16, `every screen was said in both languages: ${said}`);
  assert.equal(screensSaid.size, 14, `each of the seven screens in both languages, the radar's among them: ${[...screensSaid].join(", ")}`);
  assert.ok(screensSaid.has("fr RadarAnswerView"));
});

test("the per-role speech test fails a screen that says an entry into force as an application", () => {
  const redated = datedByItsEntryIntoForce("evidence_bundle", census.envelopes.find((entry) => entry.operation === "evidence_bundle" && entry.envelope.result?.value?.publisher === "lu-legilux").envelope);
  const markup = renderToStaticMarkup(h(ReadingAnswerView, { outcome: readingOutcome({ state: "success", envelope: redated.envelope }) }));
  assert.deepEqual(applicationSaidOf(markup, redated.date), []);
  // The heading said as S7a's predecessor said it, and the French of it: both caught.
  const borrowed = markup.replace(`dated by its entry into force on ${redated.date}`, `the state applying from ${redated.date}`);
  assert.notEqual(borrowed, markup, "the heading says the entry into force");
  assert.notDeepEqual(applicationSaidOf(borrowed, redated.date), []);
  for (const words of ["applicable from", "applies from", "la version applicable à partir du", "s’applique à partir du", "applicable jusqu’au",
    "loi-1991-08-10-n3, fra, from", "loi-1991-08-10-n3, fra, à partir du", "Carried by 1 held state, from"]) {
    assert.notDeepEqual(applicationSaidOf(`<p>${words} ${redated.date}</p>`, redated.date), [], words);
  }
  for (const words of ["entry into force on", "daté de son entrée en vigueur le", "entrée en vigueur :", "Read on"]) {
    assert.deepEqual(applicationSaidOf(`<p>${words} ${redated.date}</p>`, redated.date), [], words);
  }
});
