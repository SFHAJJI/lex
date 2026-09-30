// The live reading screen, measured against what the platform really sent.
//
// The screen asks `evidence_bundle` through the client module when the reader submits, and shows one
// state. These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: the fixture state's bundle and four refusals), and
// hold the refusal cards for the codes those envelopes do not reach to the payloads the platform
// sends for them (`schemas/v3-platform/refusal-payload-samples.json`).

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { LiveReading, ReadingAnswerView, RefusalCard, renderLiveReadingPage } from "../.react-build/app.mjs";
import {
  LIVE_READING_IDLE,
  LIVE_READING_REFUSAL_SENTENCES,
  createReadingSession,
  loadLiveReading,
  quotationLanguageTag,
  readingOutcome,
  readingParameters,
} from "../scripts/live-reading.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const payloads = JSON.parse(await readFile(new URL("../../schemas/v3-platform/refusal-payload-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "evidence_bundle" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the evidence_bundle envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "the work on its state's date";
const REQUEST = { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2024-02-01", language: "fra" };

function answering(status, contentType, body) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return {
      status,
      headers: { get: (name) => (name.toLowerCase() === "content-type" ? contentType : null) },
      text: async () => (typeof body === "string" ? body : JSON.stringify(body)),
    };
  };
  return { calls, fetchImpl };
}

const view = (outcome) => renderToStaticMarkup(h(ReadingAnswerView, { outcome }));

test("the server renders the form in its idle state with no named control, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveReading, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_READING_IDLE));
  assert.equal(calls.length, 0);
  const form = markup.slice(markup.indexOf("<form"), markup.indexOf("</form>"));
  assert.doesNotMatch(form, /\sname=/);
  const page = renderLiveReadingPage();
  assert.ok(page.includes('<script src="/client-live-reading.js" defer=""></script>'));
  assert.match(page, /id="live-reading-root"/);
});

test("the request carries the identifier and the date as typed, and a language only when one is chosen", () => {
  assert.deepEqual(readingParameters(REQUEST), REQUEST);
  assert.deepEqual(readingParameters({ identifier: "x", date: "2024-02-01" }), { identifier: "x", date: "2024-02-01" });
  for (const [what, request, reason] of [
    ["no identifier", { identifier: " ", date: "2024-02-01" }, /needs the identifier of a work/],
    ["no date", { identifier: "x", date: "" }, /calendar date, written yyyy-mm-dd/],
    ["a date that is not one", { identifier: "x", date: "2024-02-30" }, /calendar date/],
    ["a date in another order", { identifier: "x", date: "01/02/2024" }, /calendar date/],
    ["a language the form does not offer", { identifier: "x", date: "2024-02-01", language: "ltz" }, /not a language this form offers/],
  ]) {
    assert.throws(() => readingParameters(request), reason, what);
  }
});

test("a served bundle is the text of the state, quoted in its language, with both dates where they differ", async () => {
  const envelope = envelopeOf(ANSWER);
  const { calls, fetchImpl } = answering(200, "application/json", envelope);
  const outcome = await loadLiveReading({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls[0].url, "/api/v3/evidence_bundle");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "evidence_bundle", parameters: REQUEST });

  const state = envelope.result.value.states[0];
  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="success">/);
  assert.equal((markup.match(/<blockquote lang="fr">/g) ?? []).length, state.articles.length, "every quotation in the state's language, never the interface's");
  for (const article of state.articles) {
    assert.ok(markup.includes(article.article_permalink), `${article.publisher_id} is pinned by its permalink`);
    assert.ok(markup.includes(article.text_sha256));
  }
  const conflicted = state.articles.filter((article) => article.validity_conflict);
  assert.equal((markup.match(/data-validity-conflict=""/g) ?? []).length, conflicted.length);
  assert.ok(markup.includes(`This article&#x27;s own date is ${conflicted[0].article_valid_from}; its state applies from ${state.applicability_date}.`));
  const noted = state.articles.find((article) => article.notes.length > 0);
  assert.ok(markup.includes(`[${noted.notes[0].marker}]`), "the notes travel beside the text");
  assert.ok(!markup.includes("in force"), "the phrase is never said");
});

test("an article held without text is named, never quoted empty", () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const state = envelope.result.value.states[0];
  const [moved] = state.articles.splice(0, 1);
  if (moved.validity_conflict) state.validity_conflict_count -= 1;
  state.articles_without_text = [{ article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id, reason: "no_text_tokens" }];
  const outcome = readingOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success");
  const markup = view(outcome);
  assert.ok(markup.includes(`Held without text: ${moved.publisher_id}.`));
  assert.ok(!markup.includes(`data-article="${moved.publisher_id}"`));
});

test("the refusals a reading from this page can meet: cards where the card's rules take the platform's payload", async () => {
  for (const [scenario, code, expected] of [
    ["an EU identifier on a mount without the EU index", "retrieval_mode_unavailable", LIVE_READING_REFUSAL_SENTENCES.retrieval_mode_unavailable],
    ["no corpus mounted", "no_corpus_mounted", "This build has no Luxembourg index mounted."],
  ]) {
    const { fetchImpl } = answering(200, "application/json", envelopeOf(scenario));
    const outcome = await loadLiveReading({ contract, fetchImpl, request: REQUEST });
    assert.equal(outcome.code, code, scenario);
    assert.equal(outcome.card, true, `${scenario}: the card's rules accept the payload the platform sent`);
    assert.equal(outcome.sentence, expected);
    const card = renderToStaticMarkup(h(RefusalCard, { code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`, scenario);
  }

  // The two absences the platform sends are cards now that its payloads carry the absence evidence
  // (driver decision (a)): what would answer them, in the closed vocabulary, `asserts_absence_of_law:
  // false`, and for identifier_unknown the population the index searched. The card says the absence
  // note, the routes out and, for no_version_for_date, the date the held history begins.
  for (const [scenario, code, shows] of [
    ["a work the index does not hold", "identifier_unknown", ["This build&#x27;s Luxembourg index holds 1 Luxembourg work", "a corrected identifier"]],
    ["a date before the work's history", "no_version_for_date", ["2024-02-01", "a new observation, if the publisher publishes this"]],
  ]) {
    const outcome = await loadLiveReading({ contract, fetchImpl: answering(200, "application/json", envelopeOf(scenario)).fetchImpl, request: REQUEST });
    assert.equal(outcome.state, "refusal", scenario);
    assert.equal(outcome.code, code);
    assert.equal(outcome.card, true, `${code} is a card: its payload carries the absence evidence`);
    const card = renderToStaticMarkup(h(RefusalCard, { code: outcome.code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`);
    for (const text of [...shows, "It is not evidence that the instrument or the law does not exist."]) {
      assert.ok(card.includes(text), `${code}: the card shows "${text}"`);
    }
  }

  // The codes the fixture's envelopes do not reach, held to the payloads the platform sends for them.
  for (const code of ["ambiguous_version", "text_withheld", "text_not_available", "language_not_available"]) {
    // text_not_available is an absence too, and its payload does carry what_would_answer and
    // asserts_absence_of_law, so its card is shown.
    const row = payloads.produced.find((entry) => entry.code === code);
    assert.ok(row, `the refusal census holds a produced ${code}`);
    const envelope = structuredClone(envelopeOf("a date before the work's history"));
    envelope.refusal.code = code;
    envelope.refusal.helpful_payload = row.payload;
    const outcome = readingOutcome({ state: "refusal", envelope });
    assert.equal(outcome.sentence, LIVE_READING_REFUSAL_SENTENCES[code], code);
    assert.equal(outcome.card, true, `${code}: the card's rules accept the platform's payload`);
    assert.doesNotThrow(() => view(outcome), code);
  }
});

test("a transport failure and an unreadable answer are each a state with a sentence", async () => {
  const problem = answering(400, "application/problem+json", { type: "about:blank", title: "bad", status: 400, code: "request_schema_invalid" });
  assert.match((await loadLiveReading({ contract, fetchImpl: problem.fetchImpl, request: REQUEST })).sentence, /refused the reading request as it was asked/);
  const broken = structuredClone(envelopeOf(ANSWER));
  broken.result.value.states[0].validity_conflict_count += 1;
  const unreadable = readingOutcome({ state: "success", envelope: broken });
  assert.equal(unreadable.state, "invalid_envelope");
  assert.match(unreadable.sentence, /articles are flagged/);
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  const session = createReadingSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ identifier: "x", date: "tomorrow" }), false);
  assert.equal(settled.calls.length, 0);
  const arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes, ["invalid_request", "loading", "success"]);

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.identifier); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createReadingSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ identifier: "first", date: "2024-02-01" });
  second.ask({ identifier: "second", date: "2024-02-01" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"]);
  assert.deepEqual(later, ["loading", "loading"]);
});

test("a quotation's language tag is the shortest ISO 639 code", () => {
  assert.equal(quotationLanguageTag("fra"), "fr");
  assert.equal(quotationLanguageTag("deu"), "de");
  assert.equal(quotationLanguageTag("ltz"), "lb");
  assert.equal(quotationLanguageTag("xyz"), "xyz");
});

test("a work read in two languages keeps each article's element id unique (review of #777)", () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const value = envelope.result.value;
  const german = structuredClone(value.states[0]);
  const sha = "d".repeat(64);
  german.language = "deu";
  german.state_sha256 = sha;
  german.permalink = `${german.stable_coordinate}--${sha}`;
  german.articles.forEach((article, index) => {
    article.language = "deu";
    article.article_identity_sha256 = index.toString(16).padStart(64, "e");
    article.article_permalink = `${german.permalink}#${article.publisher_id}`;
  });
  value.states.unshift(german);
  value.available_languages = ["deu", "fra"];
  value.requested_language = null;
  const outcome = readingOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const ids = [...view(outcome).matchAll(/<li id="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(ids.length, 2 * value.states[1].articles.length);
  assert.equal(new Set(ids).size, ids.length);
  assert.ok(ids.includes("deu-art_15") && ids.includes("fra-art_15"));
});
