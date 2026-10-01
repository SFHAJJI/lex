// The live dossier screen, measured against envelopes the platform really sent.
//
// The screen asks `dossier` through the client module when the reader submits, and shows one state.
// These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: the fixture work's dossier, and the four refusals a
// request from this page can meet), so every state is the one a real server response produces, and
// the server render is the idle form a browser hydrates.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView, LiveDossier, RefusalCard, renderLiveDossierPage } from "../.react-build/app.mjs";
import {
  LIVE_DOSSIER_IDLE,
  LIVE_DOSSIER_REFUSAL_SENTENCES,
  createDossierSession,
  dossierOutcome,
  dossierParameters,
  loadLiveDossier,
} from "../scripts/live-dossier.mjs";
import { noCorpusMountedSentence } from "../scripts/live-refusals.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "dossier" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the dossier envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "the work, in the language it is held in";
const REQUEST = { identifier: "/lu-legilux/loi-1991-08-10-n3", language: "fra" };

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

const view = (outcome) => renderToStaticMarkup(h(DossierAnswerView, { outcome }));
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/\s+/g, " ");

test("the server renders the form in its idle state with no named control, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveDossier, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_DOSSIER_IDLE));
  assert.equal(calls.length, 0);
  const form = markup.slice(markup.indexOf("<form"), markup.indexOf("</form>"));
  assert.doesNotMatch(form, /\sname=/, "a submit the browser performs before hydration sends nothing");

  const page = renderLiveDossierPage();
  assert.match(page, /<script src="\/client-live-dossier.js" defer=""><\/script>/);
  assert.match(page, /id="live-dossier-root"/);
});

test("the request carries the identifier as typed and a language only when one is chosen", () => {
  assert.deepEqual(dossierParameters(REQUEST), REQUEST);
  assert.deepEqual(dossierParameters({ identifier: " /lu-legilux/x " }), { identifier: " /lu-legilux/x " }, "any held language: no language sent");
  assert.deepEqual(dossierParameters({ identifier: "x", language: "" }), { identifier: "x" });
  assert.throws(() => dossierParameters({ identifier: "  " }), /needs the identifier of a work/);
  assert.throws(() => dossierParameters({ identifier: "x", language: "ltz" }), /not a language this form offers/);
});

test("a served dossier is the work, its states and what it does not hold, read by the dossier reader", async () => {
  const envelope = envelopeOf(ANSWER);
  const { calls, fetchImpl } = answering(200, "application/json; charset=utf-8", envelope);
  const outcome = await loadLiveDossier({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls[0].url, "/api/v3/dossier");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "dossier", parameters: REQUEST });

  const value = envelope.result.value;
  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="success">/);
  const shown = text(markup);
  assert.ok(shown.includes(value.work_key));
  assert.ok(markup.includes(value.states[0].permalink), "each state is pinned by its permalink");
  assert.ok(shown.includes("1 state in fra, from 2024-02-01 to 2024-02-01."));
  assert.ok(shown.includes("This index holds no title for this work."), "no title is said, not left blank");
  for (const row of value.not_held) {
    assert.ok(shown.includes(row.item), `the dossier says it does not hold ${row.item}`);
  }
  assert.equal((markup.match(/<li><strong>/g) ?? []).length, value.not_held.length);
});

test("an EU work's dossier is the work by its CELEX, its expressions with the one wording held of each, pinned, and what it does not hold", async () => {
  const envelope = envelopeOf("one EU work by its CELEX");
  const value = envelope.result.value;
  const request = { identifier: "32016R0679" };
  const { calls, fetchImpl } = answering(200, "application/json", envelope);
  const outcome = await loadLiveDossier({ contract, fetchImpl, request });
  assert.equal(outcome.state, "success", outcome.sentence);
  assert.equal(outcome.view.publisher, "eu-eurlex");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "dossier", parameters: request });

  const markup = view(outcome);
  const shown = text(markup);
  assert.ok(markup.includes("<h2>32016R0679</h2>"), "the work is named by its CELEX");
  assert.ok(markup.includes(`<code>${value.publisher_work_id}</code>`));
  assert.ok(shown.includes("1 expression held, in its one original wording."));
  const [expression] = value.expressions;
  assert.ok(markup.includes(`data-pinned-wording="${expression.pinned_wording.wording_sha256}"`));
  assert.ok(markup.includes(`<code>${expression.pinned_wording.permalink}</code>`), "the expression is pinned by its wording's permalink");
  assert.ok(markup.includes("<th scope=\"col\">Wording date</th>"), "the date is a wording date, never an applicability date");
  assert.doesNotMatch(shown, /Applies from|Next state from/, "an EU dossier lists no state");
  assert.doesNotMatch(markup, /data-state=/);
  assert.ok(shown.includes("The wording date (wording_date in search, wording_dates in dossier) is the date the publisher's Formex package gives the act"));
  for (const row of value.not_held) assert.ok(shown.includes(row.item), `the dossier says it does not hold ${row.item}`);

  const unpinned = structuredClone(envelope);
  unpinned.result.value.expressions[0].pinned_wording = null;
  const refused = dossierOutcome({ state: "success", envelope: unpinned });
  assert.equal(refused.state, "invalid_envelope", "an expression that pins no wording is not shown unpinned");
  assert.match(refused.sentence, /pins no wording/);
});

test("an answer the reader refuses is said as unreadable", () => {
  const broken = structuredClone(envelopeOf(ANSWER));
  broken.result.value.state_count = 2;
  const unreadable = dossierOutcome({ state: "success", envelope: broken });
  assert.equal(unreadable.state, "invalid_envelope");
  assert.match(unreadable.sentence, /state_count is 2/);
});

test("an unknown work is a card that says how much the index searched", async () => {
  // The disagreement `refusal-card.mjs` recorded for identifier_unknown is settled where it belongs:
  // the platform's payload now carries `population_disclosure`, counted from the mounted index, and
  // the absence evidence, so "not found" is said against the size of what was searched.
  const envelope = envelopeOf("a work the index does not hold");
  const { fetchImpl } = answering(200, "application/json", envelope);
  const outcome = await loadLiveDossier({ contract, fetchImpl, request: { identifier: "/lu-legilux/no-such-work" } });
  assert.equal(outcome.state, "refusal");
  assert.equal(outcome.code, "identifier_unknown");
  assert.equal(outcome.card, true);
  const card = renderToStaticMarkup(h(RefusalCard, { code: outcome.code, sentence: outcome.sentence, payload: outcome.payload }));
  assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`);
  assert.ok(card.includes(envelope.refusal.helpful_payload.population_disclosure.replaceAll("'", "&#x27;")), "the population the index searched");
  assert.ok(card.includes("It is not evidence that the instrument or the law does not exist."));
});

test("the other refusals a dossier from this page can meet are refusal cards with the platform's payloads", async () => {
  for (const [scenario, code, expected] of [
    ["a language the work is not held in", "language_not_available", LIVE_DOSSIER_REFUSAL_SENTENCES.language_not_available],
    ["an EU identifier on a mount without the EU index", "no_corpus_mounted", "This build has no EU index mounted."],
    ["no corpus mounted", "no_corpus_mounted", "This build has no Luxembourg index mounted."],
  ]) {
    const envelope = envelopeOf(scenario);
    const { fetchImpl } = answering(200, "application/json", envelope);
    const outcome = await loadLiveDossier({ contract, fetchImpl, request: { identifier: "x" } });
    assert.equal(outcome.state, "refusal", scenario);
    assert.equal(outcome.code, code, scenario);
    assert.equal(outcome.card, true, `${scenario}: the card's rules accept the payload the platform sent`);
    assert.equal(outcome.sentence, expected, `${scenario}: a server holding the Luxembourg index is never said to hold none (review of #775)`);
    const card = renderToStaticMarkup(h(RefusalCard, { code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`, scenario);
  }
  assert.equal(LIVE_DOSSIER_REFUSAL_SENTENCES.no_corpus_mounted, undefined, "the missing index is named from the payload");
});

test("a transport failure is a state with a sentence, a refused request is said as refused", async () => {
  const problem = answering(400, "application/problem+json", { type: "about:blank", title: "bad", status: 400, code: "request_schema_invalid" });
  const refused = await loadLiveDossier({ contract, fetchImpl: problem.fetchImpl, request: REQUEST });
  assert.equal(refused.state, "transport_failure");
  assert.match(refused.sentence, /refused the dossier request as it was asked/);
  const offline = await loadLiveDossier({ contract, fetchImpl: async () => { throw new TypeError("failed"); }, request: REQUEST });
  assert.match(offline.sentence, /could not be reached \(network_error\)/);
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  const session = createDossierSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ identifier: "" }), false);
  assert.deepEqual(outcomes, ["invalid_request"]);
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
  const second = createDossierSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ identifier: "first" });
  second.ask({ identifier: "second" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"]);
  assert.deepEqual(later, ["loading", "loading"], "nothing settles after a cancel");
});

test("a missing index is named by the corpus the refusal says it needed, never as no index at all (review of #775)", () => {
  assert.equal(noCorpusMountedSentence({ required_corpus: "lu" }), "This build has no Luxembourg index mounted.");
  assert.equal(noCorpusMountedSentence({ required_corpus: "eu" }), "This build has no EU index mounted.");
  assert.equal(noCorpusMountedSentence({ required_corpus: "xx" }), "This build has no index mounted for this request's publisher.");
  assert.equal(noCorpusMountedSentence(undefined), "This build has no index mounted for this request's publisher.");
});
