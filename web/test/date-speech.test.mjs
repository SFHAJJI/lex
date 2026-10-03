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
//   Luxembourg answer use no EU date words, and an EU answer's no Luxembourg ones.
//
// The platform's own sentences are data here, as in the chrome scan: an EU answer says, in its own
// words, that its date is never merged with a Luxembourg applicability date, and that sentence
// naming the other kind is the promise kept, not broken.

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  CompareAnswerView,
  CoverageAnswerView,
  DossierAnswerView,
  ExportAnswerView,
  HistoryAnswerView,
  RadarAnswerView,
  ReadingAnswerView,
  SearchAnswerView,
} from "../.react-build/app.mjs";
import { LIVE_CHROME, REFUSAL_TRANSLATIONS, entriesOf } from "../scripts/live-chrome.mjs";
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
 * EU-only entries the review of the French found outside the first set (the export's sentence, the
 * reading's counts, the refusal card's declared nulls and its offered wordings).
 */
const EUROPE_ENTRIES = new Set([
  "search.euWording", "search.euHit", "search.notHeldHeading", "dossier.euExpressions.one", "dossier.euExpressions.other",
  "dossier.wordingDate", "reading.europeWordingHeading", "reading.europeOriginalHeading", "reading.europeConsolidatedHeading",
  "reading.europeHoldsUntil", "reading.europeLatest", "reading.europeCounts.one", "reading.europeCounts.other",
  "export.europeNotComposed", "refusalCard.europeNullSentences.nearest_earlier", "refusalCard.europeNullSentences.nearest_later",
  "refusalCard.europeCandidate", "refusalCard.europeCandidateWithdrawalNotStated", "refusalCard.europePublished",
  "refusalCard.europeNotes.ambiguous_version",
  "reading.europeSameDateWorks.one", "reading.europeSameDateWorks.other", "reading.europeUnplaced.one", "reading.europeUnplaced.other",
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
  assert.ok(spokenBy["lu-legilux"].some((said) => said.startsWith("search:") && /version of/.test(said)), "a Luxembourg search hit is a version of its date");
  assert.ok(spokenBy["lu-legilux"].some((said) => said.startsWith("dossier:") && /Applies from/.test(said)), "a Luxembourg dossier's date is when a state applies from");
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
