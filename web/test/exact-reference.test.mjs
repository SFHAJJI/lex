// Exact references first (the plan's item 4.2), on the home box and the search page: the answers the census captured
// for a query the platform read as an exact reference, driven through the search screen's client and laid out by its view.
// A reference the platform holds is the `opens` state, whose link is the act's address under this page's language (the
// screen goes there); one whose date names no day is `not_opened`; a refusal of the phase says what was recognised above
// its card, with the phase's own sentences. The date is printed back in words, as it was read.
//
// The answers are `schemas/v3-platform/answer-samples.json`'s (driven by the real handler on the fixture mounts); each is
// put in an envelope the census holds for the same publisher, so the client reads it as a served answer.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { SearchAnswerView } from "../.react-build/app.mjs";
import {
  LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES,
  LIVE_SEARCH_REFUSAL_SENTENCES,
  loadLiveSearch,
  longDate,
  searchOutcome,
} from "../scripts/live-search.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { REFUSALS_FR } from "../scripts/live-chrome-fr.mjs";
import { validateRefusal } from "../scripts/refusal-card.mjs";

const PLACEHOLDER = "<varies-per-run>";
const read = async (name) => JSON.parse(await readFile(new URL(`../../schemas/v3-platform/${name}`, import.meta.url), "utf8"));
const answers = await read("answer-samples.json");
const census = await read("envelope-samples.json");
const refusals = await read("refusal-payload-samples.json");
const { contract } = census;

/** Fills the fields the census normalises, each with its own digest. */
function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") {
    return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  }
  if (node === PLACEHOLDER) {
    counter.n += 1;
    return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64);
  }
  return node;
}

/** The captured reference answer whose scenario starts so. */
function referenceAnswer(scenarioStart) {
  const row = answers.sampled.find((sample) => sample.operation === "search" && sample.scenario.startsWith(scenarioStart));
  assert.ok(row, `the census holds the reference answer "${scenarioStart}..."`);
  assert.ok(["opens", "not_opened"].includes(row.answer.reference.outcome));
  return withDigests(row.answer);
}

/** A census search envelope of the answer's publisher, carrying the answer. */
function envelopeWith(answer) {
  const template = census.envelopes.find((entry) => entry.operation === "search" && entry.envelope.result?.value?.publisher === answer.publisher);
  assert.ok(template, `the census holds a ${answer.publisher} search answer envelope`);
  const envelope = structuredClone(template.envelope);
  envelope.result.value = answer;
  return envelope;
}

function answering(envelope) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return { status: 200, headers: { get: (name) => (name.toLowerCase() === "content-type" ? "application/json" : null) }, text: async () => JSON.stringify(envelope) };
  };
  return { calls, fetchImpl };
}

const view = (outcome) => renderToStaticMarkup(h(SearchAnswerView, { outcome }));
const text = (markup) => markup.replace(/<[^>]+>/g, "").replaceAll("&quot;", '"').replaceAll("&#x27;", "'").replaceAll("&amp;", "&");

test("a reference the platform holds opens its act: the recognition, the address, under this page's language", async () => {
  for (const [scenario, query, recognised, opening, href] of [
    ["an article of that act at a date", "article 15 de la loi du 10 août 1991, 15 mars 2024",
      "Recognised: loi du 10 août 1991, article 15, at 15 March 2024.", "Opening the reading: ",
      /^\/en\/lu-legilux\/loi-1991-08-10-n3\/2024-02-01--[0-9a-f]{64}\/fra\/art_15$/],
    ["the same article with no date", "article 15 de la loi du 10 août 1991",
      "Recognised: loi du 10 août 1991, article 15.", "Opening the reading: ", /^\/en\/lu-legilux\/loi-1991-08-10-n3\/fra\/art_15$/],
    ["an act by its type and date", "loi du 10 août 1991",
      "Recognised: loi du 10 août 1991.", "Opening the dossier: ", /^\/en\/lu-legilux\/loi-1991-08-10-n3$/],
    ["an EU act by its conventional name", "Regulation (EU) 2016/679",
      "Recognised: Regulation (EU) 2016/679 (CELEX 32016R0679).", "Opening the dossier: ", /^\/en\/eu-eurlex\/32016R0679$/],
    ["an article of the EU act with no date", "Article 99 of Regulation (EU) 2016/679",
      "Recognised: Regulation (EU) 2016/679 (CELEX 32016R0679), article 99.", "Opening the reading: ", /^\/en\/eu-eurlex\/32016R0679\/eng\/099$/],
  ]) {
    const answer = referenceAnswer(scenario);
    const { calls, fetchImpl } = answering(envelopeWith(answer));
    const outcome = await loadLiveSearch({ contract, fetchImpl, request: { query, language: answer.requested_language } });
    assert.equal(outcome.state, "opens", scenario);
    assert.equal(calls.length, 1, `${scenario}: one request, the search; the phase ran on the server`);
    assert.match(outcome.href, href, scenario);
    const markup = view(outcome);
    assert.match(markup, /^<section data-answer-state="opens">/, scenario);
    const shown = text(markup);
    assert.ok(shown.includes(recognised), `${scenario}: ${shown}`);
    assert.ok(shown.includes(opening + outcome.view.opens.address), `${scenario}: the address is said and linked`);
    assert.ok(markup.includes(`<a href="${outcome.href}">`), scenario);
    assert.ok(markup.includes(`data-opens="${outcome.view.opens.addressKind}"`), scenario);
    assert.doesNotMatch(markup, /data-lane=/, `${scenario}: no hit is listed`);
  }
});

test("a date the reference does not name is said apart, as the date read: today in Luxembourg or the search's", () => {
  const undated = referenceAnswer("the same article with no date");
  const shown = text(view(searchOutcome({ state: "success", envelope: envelopeWith(undated) })));
  assert.equal(undated.reference.recognised.date.source, "today_in_luxembourg");
  assert.ok(shown.includes(`No date was given: the date read is today in Luxembourg, ${longDate(undated.reference.recognised.date.date, "en")}.`), shown);
  const searchDated = structuredClone(undated);
  searchDated.reference.recognised.date = { as_typed: null, date: "2026-09-18", source: "search_date" };
  assert.ok(text(view(searchOutcome({ state: "success", envelope: envelopeWith(searchDated) })))
    .includes("The reference names no date: the date read is the search's, 18 September 2026."));
});

test("a reference whose date names no day of the calendar opens nothing, and says the date as typed", () => {
  const outcome = searchOutcome({ state: "success", envelope: envelopeWith(referenceAnswer("a citation whose date names no calendar day")) });
  assert.equal(outcome.state, "not_opened");
  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="not_opened">/);
  const shown = text(markup);
  assert.ok(shown.includes("Recognised: loi du 10 août 1991, article 15."), shown);
  assert.ok(shown.includes("“31 février 2024” names no day of the calendar, so nothing was opened and no word was searched."), shown);
  const twoDates = structuredClone(outcome.view);
  assert.ok(text(view({ ...outcome, view: { ...twoDates, reason: "two_dates_disagree" } }))
    .includes("The reference names two dates that disagree, so nothing was opened and no word was searched."));
});

test("a refusal of the phase says what was recognised above its card, with the phase's own sentence", () => {
  const template = census.envelopes.find((entry) => entry.operation === "search" && entry.envelope.refusal?.code === "language_not_available");
  assert.ok(template, "the census holds a search refusal envelope");
  for (const row of refusals.produced.filter((candidate) => Object.hasOwn(candidate.payload, "recognised_kind"))) {
    const envelope = structuredClone(template.envelope);
    envelope.refusal.code = row.code;
    envelope.refusal.helpful_payload = row.payload;
    const outcome = searchOutcome({ state: "refusal", envelope });
    assert.equal(outcome.state, "refusal", row.code);
    assert.equal(outcome.card, true, `${row.code}: the card accepts what was recognised`);
    assert.equal(outcome.sentence, LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES[row.code], row.code);
    assert.equal(outcome.recognition.act, row.payload.recognised_act, row.code);
    const shown = text(view(outcome));
    assert.ok(shown.includes(`Recognised: ${row.payload.recognised_title ?? row.payload.recognised_act}`), `${row.code}: ${shown}`);
    assert.ok(shown.includes(outcome.sentence), row.code);
    assert.ok(shown.indexOf("Recognised:") < shown.indexOf(outcome.sentence), `${row.code}: the recognition above the card`);
  }
  // A refusal of a word search says nothing recognised, and its sentence is the search's.
  const plain = searchOutcome({ state: "refusal", envelope: template.envelope });
  assert.equal(plain.recognition, null);
  assert.equal(plain.sentence, LIVE_SEARCH_REFUSAL_SENTENCES.language_not_available);
  assert.doesNotMatch(text(view(plain)), /Recognised:/);
});

test("every sentence of the phase has its reviewed French, and the refusal card accepts what was recognised", () => {
  for (const sentence of new Set(Object.values(LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES))) {
    assert.ok(Object.hasOwn(REFUSALS_FR.sentences, sentence), `no reviewed French for "${sentence}"`);
  }
  for (const code of ["identifier_unknown", "anchor_not_in_version", "no_version_for_date", "ambiguous_version"]) {
    const row = refusals.produced.find((candidate) => candidate.code === code);
    assert.ok(Object.hasOwn(row.payload, "recognised_act"), `${code}: the census's richest sample is the phase's`);
    assert.doesNotThrow(() => validateRefusal({ code, sentence: LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES[code], payload: row.payload }), code);
  }
});

test("a date is printed back in words in the page's language, the first of a month as each writes it", () => {
  assert.equal(longDate("2021-03-15", "en"), "15 March 2021");
  assert.equal(longDate("2021-03-01", "en"), "1 March 2021");
  assert.equal(longDate("2021-03-15", "fr"), "15 mars 2021");
  assert.equal(longDate("2021-03-01", "fr"), "1er mars 2021");
  assert.equal(longDate("1991-08-10", "fr"), "10 août 1991");
  assert.equal(longDate("2020-12-31", "en"), "31 December 2020");
  assert.throws(() => longDate("2021-02-30", "en"), /not a calendar date/);
  for (const language of ["en", "fr"]) assert.equal(liveChrome(language).common.months.length, 12, language);
  assert.equal(liveChrome("fr").search.recognisedArticleDate, "Référence reconnue : {act}, article {article}, au {date}.");
});
