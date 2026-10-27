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
  LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES,
  LIVE_SEARCH_HELD_ACT_REFUSAL_SENTENCES,
  LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES,
  LIVE_SEARCH_REFUSAL_SENTENCES,
  loadLiveSearch,
  longDate,
  searchOutcome,
} from "../scripts/live-search.mjs";
import { LIVE_READING_EUROPE_REFUSAL_SENTENCES } from "../scripts/live-reading.mjs";
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

/** The census's search refusal envelope, carrying a refusal of the phase as its publisher's context names it. */
function refusalEnvelope(code, payload, publisher = "lu-legilux") {
  const template = census.envelopes.find((entry) => entry.operation === "search" && entry.envelope.refusal?.code === "language_not_available");
  assert.ok(template, "the census holds a search refusal envelope");
  const envelope = structuredClone(template.envelope);
  envelope.refusal.code = code;
  envelope.refusal.helpful_payload = payload;
  envelope.context.publisher = publisher;
  return envelope;
}

/** The census's samples of a code (its richest payload and the others it keeps), each a payload. */
function samplesOf(code) {
  const row = refusals.produced.find((candidate) => candidate.code === code);
  assert.ok(row, `the census holds ${code}`);
  return [row.payload, ...(row.more_payloads ?? [])];
}

/** Whether a recognised act is an EU act's: the census's EU references name a CELEX. */
const europeAct = (payload) => /CELEX|^[0-9]{5}[A-Z][0-9]{4}/.test(payload.recognised_act);
const text = (markup) => markup.replace(/<[^>]+>/g, "").replaceAll("&quot;", '"').replaceAll("&#x27;", "'").replaceAll("&amp;", "&");

test("a reference the platform holds opens its act: the recognition, the address, under this page's language", async () => {
  for (const [scenario, query, recognised, opening, href] of [
    ["an article of that act at a date", "article 15 de la loi du 10 août 1991, 15 mars 2024",
      "Recognised: loi du 10 août 1991, article 15, at 15 March 2024.", "This reference opens the reading: ",
      /^\/en\/lu-legilux\/loi-1991-08-10-n3\/2024-02-01--[0-9a-f]{64}\/fra\/art_15$/],
    ["the same article with no date", "article 15 de la loi du 10 août 1991",
      "Recognised: loi du 10 août 1991, article 15.", "This reference opens the reading: ", /^\/en\/lu-legilux\/loi-1991-08-10-n3\/fra\/art_15$/],
    ["an act by its type and date", "loi du 10 août 1991",
      "Recognised: loi du 10 août 1991.", "This reference opens the dossier: ", /^\/en\/lu-legilux\/loi-1991-08-10-n3$/],
    ["an EU act by its conventional name", "Regulation (EU) 2016/679",
      "Recognised: Regulation (EU) 2016/679 (CELEX 32016R0679).", "This reference opens the dossier: ", /^\/en\/eu-eurlex\/32016R0679$/],
    ["an article of the EU act with no date", "Article 99 of Regulation (EU) 2016/679",
      "Recognised: Regulation (EU) 2016/679 (CELEX 32016R0679), article 99.", "This reference opens the reading: ", /^\/en\/eu-eurlex\/32016R0679\/eng\/099$/],
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

test("a refusal of the phase says what was recognised above its card, with the phase's own sentence for its publisher", () => {
  const seen = new Set();
  for (const row of refusals.produced) {
    for (const payload of [row.payload, ...(row.more_payloads ?? [])].filter((candidate) => Object.hasOwn(candidate, "recognised_kind"))) {
      // The review of #974, F4 and F5: an EU act's refusal is said with the EU's sentences, and every code the phase
      // refuses with (text_not_available and retrieval_mode_unavailable among them) has its card.
      const europe = europeAct(payload);
      const outcome = searchOutcome({ state: "refusal", envelope: refusalEnvelope(row.code, payload, europe ? "eu-eurlex" : "lu-legilux") });
      const table = europe ? LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES : LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES;
      assert.equal(outcome.state, "refusal", row.code);
      assert.equal(outcome.card, true, `${row.code}: the card accepts what was recognised: ${outcome.sentence}`);
      // A version the reference names that is held and does not cover the date has its own head (the French review of
      // #987's repair round): "no state applies" would be false there.
      const outside = row.code === "no_version_for_date" && payload.reason === "outside_named_version";
      assert.equal(outcome.sentence, outside ? LIVE_SEARCH_HELD_ACT_REFUSAL_SENTENCES["lu-legilux"].outside : table[row.code], row.code);
      assert.ok(Object.hasOwn(REFUSALS_FR.sentences, outcome.sentence), `${row.code}: no reviewed French`);
      assert.equal(outcome.recognition.act, payload.recognised_act, row.code);
      const shown = text(view(outcome));
      assert.ok(shown.includes(`Recognised: ${payload.recognised_title ?? payload.recognised_act}`), `${row.code}: ${shown}`);
      assert.ok(shown.includes(outcome.sentence), row.code);
      assert.ok(shown.indexOf("Recognised:") < shown.indexOf(outcome.sentence), `${row.code}: the recognition above the card`);
      seen.add(`${europe ? "eu" : "lu"}:${row.code}${outside ? ":outside_named_version" : ""}`);
    }
  }
  for (const code of ["eu:text_not_available", "eu:retrieval_mode_unavailable", "lu:ambiguous_identifier", "lu:identifier_unknown", "lu:anchor_not_in_version",
    "lu:no_version_for_date:outside_named_version"]) {
    assert.ok(seen.has(code), `the census drives the phase's ${code}`);
  }
  // A refusal of a word search says nothing recognised, and its sentence is the search's.
  const template = census.envelopes.find((entry) => entry.operation === "search" && entry.envelope.refusal?.code === "language_not_available");
  const plain = searchOutcome({ state: "refusal", envelope: template.envelope });
  assert.equal(plain.recognition, null);
  assert.equal(plain.sentence, LIVE_SEARCH_REFUSAL_SENTENCES.language_not_available);
  assert.doesNotMatch(text(view(plain)), /Recognised:/);
});

test("every sentence of the phase has its reviewed French, and the refusal card accepts what was recognised", () => {
  for (const sentence of new Set([...Object.values(LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES), ...Object.values(LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES)])) {
    assert.ok(Object.hasOwn(REFUSALS_FR.sentences, sentence), `no reviewed French for "${sentence}"`);
  }
  // An EU date is a wording's: the EU table says the time view's refusals as the reading does for an EU act, never "applies".
  for (const code of ["language_not_available", "no_version_for_date", "ambiguous_version", "text_not_available"]) {
    assert.equal(LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES[code], LIVE_READING_EUROPE_REFUSAL_SENTENCES[code], code);
  }
  for (const sentence of Object.values(LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES)) {
    assert.doesNotMatch(sentence, /appl(y|ies|icable)|\bversion\b/i, sentence);
  }
  assert.doesNotMatch(LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES.ambiguous_identifier, /holds/, "a list names acts held or not");
  for (const code of ["identifier_unknown", "anchor_not_in_version", "no_version_for_date", "ambiguous_version"]) {
    const row = refusals.produced.find((candidate) => candidate.code === code);
    assert.ok(Object.hasOwn(row.payload, "recognised_act"), `${code}: the census's richest sample is the phase's`);
    assert.doesNotThrow(() => validateRefusal({ code, sentence: LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES[code], payload: row.payload }), code);
  }
});

test("a day or an issue that names several acts lists each, a held one linked, one only the catalogue names said as not held", () => {
  // The review of #974, F2: the census's Mémorial issue that publishes the fixture's act and an act the index holds no
  // version of. None is opened on its own.
  const payload = samplesOf("ambiguous_identifier").find((candidate) => Object.hasOwn(candidate, "candidate_held"));
  assert.ok(payload, "the census holds the phase's list");
  assert.deepEqual(payload.candidate_held, [true, false]);
  const outcome = searchOutcome({ state: "refusal", envelope: refusalEnvelope("ambiguous_identifier", payload) });
  assert.equal(outcome.card, true);
  assert.equal(outcome.candidates.length, 2);
  const [held, notHeld] = outcome.candidates;
  assert.equal(held.href, `/en${payload.candidates[0]}`, "a held act is linked under this page's language");
  assert.equal(notHeld.href, null, "an act this site holds no version of is not linked");
  const markup = view(outcome);
  assert.match(markup, /data-reference-candidates="2"/);
  assert.ok(markup.includes(`<a href="${held.href}">`), markup);
  const shown = text(markup);
  assert.ok(shown.includes(LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES.ambiguous_identifier), shown);
  assert.ok(shown.includes(`${payload.candidate_titles[1]} (this site holds no version of it)`), shown);
  assert.ok(shown.includes(payload.candidates[0]), "an untitled held act is named by its address");
  assert.ok(markup.includes(`<span lang="fr">${payload.candidate_titles[1]}</span>`), "a title is marked in its language");
  assert.doesNotMatch(markup, /data-answer-state="opens"/);

  // A list whose sizes disagree is not read; one that says a held act by no address of this site is not either.
  for (const broken of [
    { ...payload, candidate_held: [true] },
    { ...payload, candidate_titles: [null, null, null] },
    { ...payload, candidate_held: [true, true], candidates: [payload.candidates[0], payload.candidates[1]] },
  ]) {
    assert.equal(searchOutcome({ state: "refusal", envelope: refusalEnvelope("ambiguous_identifier", broken) }).state, "invalid_envelope");
  }
});

test("an act the publisher's catalogue names and this site does not hold is named under its card", () => {
  const [payload] = samplesOf("identifier_unknown").filter((candidate) => Object.hasOwn(candidate, "recognised_kind"));
  const catalogued = { ...payload, recognised_catalogue_works: ["http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5"] };
  const outcome = searchOutcome({ state: "refusal", envelope: refusalEnvelope("identifier_unknown", catalogued) });
  assert.equal(outcome.card, true, outcome.sentence);
  assert.deepEqual(outcome.recognition.catalogueWorks, catalogued.recognised_catalogue_works);
  assert.ok(text(view(outcome)).includes(
    "The publisher's record names this act (http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5), and this site holds no version of it."));
  assert.doesNotMatch(text(view(searchOutcome({ state: "refusal", envelope: refusalEnvelope("identifier_unknown", payload) }))), /record names this act/);
});

test("an absent version or article of an act this site holds is said as such, never as an act this site does not hold", () => {
  const [payload] = samplesOf("identifier_unknown").filter((candidate) => Object.hasOwn(candidate, "recognised_kind"));
  const luVersion = "/lu-legilux/loi-1991-08-10-n3/2024-02-01--" + "a".repeat(64) + "/fra";
  const euWording = "/eu-eurlex/32016R0679/2016-04-27--" + "b".repeat(64) + "/eng";
  for (const [publisher, address, version, kind] of [
    ["lu-legilux", "/lu-legilux/loi-1991-08-10-n3", null, "version"],
    ["lu-legilux", "/lu-legilux/loi-1991-08-10-n3", luVersion, "article"],
    ["eu-eurlex", "/eu-eurlex/32016R0679", euWording, "article"],
  ]) {
    const outcome = searchOutcome({
      state: "refusal",
      envelope: refusalEnvelope("identifier_unknown", { ...payload, recognised_held_act: address, recognised_held_version: version }, publisher),
    });
    assert.equal(outcome.card, true, outcome.sentence);
    assert.equal(outcome.recognition.heldAct, address);
    assert.equal(outcome.recognition.heldVersion, version);
    assert.equal(outcome.sentence, LIVE_SEARCH_HELD_ACT_REFUSAL_SENTENCES[publisher][kind], `${publisher} ${kind}`);
    assert.ok(Object.hasOwn(REFUSALS_FR.sentences, outcome.sentence), `${publisher} ${kind}: no reviewed French`);
  }
  assert.equal(searchOutcome({ state: "refusal", envelope: refusalEnvelope("identifier_unknown", { ...payload, recognised_held_act: null }) }).sentence,
    LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES.identifier_unknown, "an act this site does not hold");
});

test("a part of an article is said with the article, and a title this site holds no act under is searched as words and said", () => {
  const answer = referenceAnswer("an article of that act at a date");
  answer.reference.recognised.article.part = "al. 3";
  const shown = text(view(searchOutcome({ state: "success", envelope: envelopeWith(answer) })));
  assert.ok(shown.includes("Recognised: loi du 10 août 1991, article 15, al. 3, at 15 March 2024."), shown);

  // A word search the phase read as a citation by a title the index holds no act under (the review of #974, F1).
  const words = answers.sampled.find((sample) => sample.operation === "search" && sample.answer.reference?.outcome === "no_reference" && sample.answer.publisher === "lu-legilux");
  assert.ok(words, "the census holds a word search");
  const titled = withDigests(structuredClone(words.answer));
  titled.reference.reason = "title_not_held";
  const outcome = searchOutcome({ state: "success", envelope: envelopeWith(titled) });
  assert.equal(outcome.state, "success");
  assert.equal(outcome.view.referenceReason, "title_not_held");
  assert.ok(text(view(outcome)).includes(liveChrome("en").search.titleNotHeld));
  assert.equal(searchOutcome({ state: "success", envelope: envelopeWith(withDigests(words.answer)) }).view.referenceReason, null);
  const unknownReason = withDigests(structuredClone(words.answer));
  unknownReason.reference.reason = "guessed";
  assert.equal(searchOutcome({ state: "success", envelope: envelopeWith(unknownReason) }).state, "invalid_envelope");
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
