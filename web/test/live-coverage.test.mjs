// The first live screen, measured against envelopes the platform really sent.
//
// The screen asks `coverage` through the client module and shows one state. These tests drive it
// with an injected fetch that answers the census envelopes, so every state is the one a real server
// response produces, and the server render is the loading state a browser hydrates.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CoverageAnswerView, LiveCoverage, RefusalCard } from "../.react-build/app.mjs";
import {
  LIVE_COVERAGE_LOADING,
  LIVE_COVERAGE_REFUSAL_SENTENCES,
  coverageOutcome,
  loadLiveCoverage,
  startLiveCoverage,
} from "../scripts/live-coverage.mjs";
import { REFUSAL_EXAMPLES } from "../scripts/refusal-catalog.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (operation, refused) => census.envelopes.find(
  (entry) => entry.operation === operation && (refused ? entry.envelope.refusal !== null : entry.envelope.result !== null),
).envelope;

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

const view = (outcome) => renderToStaticMarkup(h(CoverageAnswerView, { outcome }));

test("the server renders the loading state, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf("coverage", false));
  const markup = renderToStaticMarkup(h(LiveCoverage, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="loading"/);
  assert.ok(markup.includes(LIVE_COVERAGE_LOADING));
  assert.equal(calls.length, 0, "the question is asked by the browser, in an effect, never while rendering");
});

test("a served coverage answer is the coverage page, read by the coverage reader", async () => {
  const envelope = envelopeOf("coverage", false);
  const { calls, fetchImpl } = answering(200, "application/json; charset=utf-8", envelope);
  const outcome = await loadLiveCoverage({ contract, fetchImpl });
  assert.equal(outcome.state, "success");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/api/v3/coverage");
  assert.equal(calls[0].init.body, JSON.stringify({ operation_id: "coverage", parameters: {} }), "no query text at all");
  const markup = view(outcome);
  assert.match(markup, /data-answer-state="success"/);
  assert.ok(markup.includes(envelope.result.value.mounted.corpus_sha256), "the page names the corpus it counted");
  assert.ok(markup.includes(envelope.result.value.mounted.index_sha256));
});

test("no corpus mounted is the refusal card, in the catalog's words", async () => {
  const envelope = envelopeOf("coverage", true);
  const { fetchImpl } = answering(200, "application/json", envelope);
  const outcome = await loadLiveCoverage({ contract, fetchImpl });
  assert.equal(outcome.state, "refusal");
  assert.equal(outcome.code, "no_corpus_mounted");
  assert.equal(outcome.sentence, REFUSAL_EXAMPLES.no_corpus_mounted.sentence, "one sentence, held equal to the catalog's");
  assert.equal(LIVE_COVERAGE_REFUSAL_SENTENCES.no_corpus_mounted, REFUSAL_EXAMPLES.no_corpus_mounted.sentence);
  const markup = view(outcome);
  const card = renderToStaticMarkup(h(RefusalCard, { code: outcome.code, sentence: outcome.sentence, payload: outcome.payload }));
  assert.equal(markup, `<section data-answer-state="refusal">${card}</section>`, "the refusal card itself, not a line that mentions it");
  assert.ok(card.includes("required_corpus"), "the card lays out the payload the platform sent");
});

test("a refusal whose card the card's rules will not show is said by its code, and the page keeps its state (review of #764)", () => {
  const envelope = structuredClone(envelopeOf("coverage", true));
  envelope.refusal.code = "identifier_unknown";
  envelope.refusal.helpful_payload = { requested_identifier: "x", official_search_actions: ["search"], what_would_answer: "y" };
  const outcome = coverageOutcome({ state: "refusal", envelope });
  assert.equal(outcome.state, "refusal");
  assert.equal(outcome.card, false);
  assert.match(outcome.sentence, /refused with identifier_unknown, and its card cannot be shown/);
  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="refusal"><p role="status">/, "a status line under the refusal state, never a render that throws");
});

test("mounting asks once, hands the state over, and a cancel aborts and silences it (review of #764)", async () => {
  const settled = answering(200, "application/json", envelopeOf("coverage", false));
  const outcomes = [];
  let done;
  const arrived = new Promise((resolve) => { done = resolve; });
  startLiveCoverage({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome); done(); } });
  await arrived;
  assert.equal(settled.calls.length, 1, "asked once");
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["success"]);

  let release;
  const signals = [];
  const pending = async (url, init) => {
    signals.push(init.signal);
    await new Promise((resolve) => { release = resolve; });
    return {
      status: 200,
      headers: { get: () => "application/json" },
      text: async () => JSON.stringify(envelopeOf("coverage", false)),
    };
  };
  const late = [];
  const cancel = startLiveCoverage({ contract, fetchImpl: pending, onOutcome: (outcome) => late.push(outcome) });
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(signals.length, 1);
  cancel();
  assert.equal(signals[0].aborted, true, "unmounting aborts the request");
  release();
  await new Promise((resolve) => setImmediate(resolve));
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(late, [], "no state is handed over after the screen is gone");
});

test("a transport failure and an unreadable answer are said as what they are", async () => {
  const notServed = answering(404, "application/problem+json", { code: "operation_not_served" });
  const failed = await loadLiveCoverage({ contract, fetchImpl: notServed.fetchImpl });
  assert.equal(failed.state, "transport_failure");
  assert.match(view(failed), /data-answer-state="transport_failure"[^]*operation_not_served/);

  const html = answering(200, "text/html", "<html>");
  const unreadable = await loadLiveCoverage({ contract, fetchImpl: html.fetchImpl });
  assert.equal(unreadable.state, "invalid_envelope");
  assert.match(view(unreadable), /data-answer-state="invalid_envelope"/);
});

test("a served answer the coverage reader cannot account for is never rendered as a page", () => {
  const envelope = structuredClone(envelopeOf("coverage", false));
  delete envelope.result.value.not_held;
  const outcome = coverageOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "invalid_envelope");
  assert.match(outcome.sentence, /could not be read as a coverage report/);
});

test("a refusal this page cannot produce is named by its code, not given another's sentence", () => {
  const envelope = structuredClone(envelopeOf("coverage", true));
  envelope.refusal.code = "language_not_available";
  envelope.refusal.helpful_payload = { requested_language: "deu", available_languages: ["fra"] };
  const outcome = coverageOutcome({ state: "refusal", envelope });
  assert.match(outcome.sentence, /refused with language_not_available/);
  assert.match(view(outcome), /data-answer-state="refusal"/);
});
