// The live provision history screen, measured against what the platform really sent.
//
// The screen asks `article_history` through the client module when the reader submits, and shows one
// state. These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: the fixture's `art_15` lineage and three refusals),
// and render a longer lineage built the way the producer builds one.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { HistoryAnswerView, LiveHistory, RefusalCard, renderLiveHistoryPage } from "../.react-build/app.mjs";
import {
  LIVE_HISTORY_IDLE,
  LIVE_HISTORY_REFUSAL_SENTENCES,
  createHistorySession,
  historyOutcome,
  historyParameters,
  loadLiveHistory,
} from "../scripts/live-history.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "article_history" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the article_history envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "one article of the work";
const REQUEST = { identifier: "/lu-legilux/loi-1991-08-10-n3", anchor: "art_15", language: "fra" };

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

const view = (outcome) => renderToStaticMarkup(h(HistoryAnswerView, { outcome }));

test("the server renders the form in its idle state with no named control, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveHistory, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.replace(/&#x27;/g, "'").includes(LIVE_HISTORY_IDLE), "the idle sentence, apostrophe decoded");
  assert.equal(calls.length, 0);
  assert.doesNotMatch(markup.slice(markup.indexOf("<form"), markup.indexOf("</form>")), /\sname=/);
  const page = renderLiveHistoryPage();
  assert.ok(page.includes('<script src="/client-live-history.js" defer=""></script>'));
});

test("the request carries the identifier and the article id as typed, and a language only when chosen", () => {
  assert.deepEqual(historyParameters(REQUEST), REQUEST);
  assert.deepEqual(historyParameters({ identifier: "x", anchor: "art_1er" }), { identifier: "x", anchor: "art_1er" });
  assert.throws(() => historyParameters({ identifier: " ", anchor: "art_1" }), /identifier of a work/);
  assert.throws(() => historyParameters({ identifier: "x", anchor: "" }), /publisher's article id/);
  assert.throws(() => historyParameters({ identifier: "x", anchor: "a", language: "ltz" }), /not a language this form offers/);
});

test("a served lineage is its rows, the states that do not carry the id, and the wording rule", async () => {
  const envelope = envelopeOf(ANSWER);
  const { calls, fetchImpl } = answering(200, "application/json", envelope);
  const outcome = await loadLiveHistory({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls[0].url, "/api/v3/article_history");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "article_history", parameters: REQUEST });
  const markup = view(outcome);
  const row = envelope.result.value.states[0];
  assert.ok(markup.includes(row.permalink));
  assert.match(markup, /data-wording="first"/);
  assert.ok(markup.includes("Carried by 1 held state in fra, from 2024-02-01; 0 held states do not carry it."));
  assert.ok(!markup.includes("in force"));
});

test("a longer lineage shows the change, the next state counting an absent one, and the absent state", () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const value = envelope.result.value;
  const first = value.states[0];
  const later = structuredClone(first);
  const sha = "e".repeat(64);
  Object.assign(later, {
    applicability_date: "2026-01-01",
    state_sha256: sha,
    stable_coordinate: `/lu-legilux/${value.work_key}/2026-01-01`,
    permalink: `/lu-legilux/${value.work_key}/2026-01-01--${sha}`,
    next_applicability_date: null,
    wording_changed: true,
  });
  later.articles = later.articles.map((article) => ({ ...article, article_identity_sha256: "f".repeat(64), wording_sha256: "e".repeat(64), validity_conflict: article.article_valid_from !== null && article.article_valid_from !== "2026-01-01" }));
  first.next_applicability_date = "2025-01-01";
  const absent = { language: "fra", applicability_date: "2025-01-01", state_sha256: "c".repeat(64), permalink: `/lu-legilux/${value.work_key}/2025-01-01--${"c".repeat(64)}` };
  value.states = [first, later];
  value.absent_in_states = [absent];
  value.wording_runs = [{ language: "fra", count: 2 }];
  value.distinct_wordings = [{ language: "fra", count: 2 }];
  const outcome = historyOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = view(outcome);
  assert.deepEqual([...markup.matchAll(/data-wording="([^"]+)"/g)].map((match) => match[1]), ["first", "changed"]);
  assert.ok(markup.includes(absent.permalink), "the state that does not carry the id is shown");
  assert.ok(markup.includes("<td>2025-01-01</td>"), "the first row's next state is the absent one");
  assert.ok(markup.includes("fra: 2 wording runs, 2 distinct"));
});

test("the refusals a lineage from this page can meet", async () => {
  const early = await loadLiveHistory({ contract, fetchImpl: answering(200, "application/json", envelopeOf("an article id no held state carries")).fetchImpl, request: REQUEST });
  assert.equal(early.code, "anchor_not_in_version");
  assert.equal(early.card, true, "the absence carries its evidence, so it is a card (driver decision (a))");
  const earlyCard = renderToStaticMarkup(h(RefusalCard, { code: early.code, sentence: early.sentence, payload: early.payload }));
  for (const id of ["art_4", "art_40", "art_41", "art_42"]) assert.ok(earlyCard.includes(id), `the card names the nearest id ${id}`);
  assert.ok(earlyCard.includes("It is not evidence that the instrument or the law does not exist."));

  const unknown = await loadLiveHistory({ contract, fetchImpl: answering(200, "application/json", envelopeOf("a work the index does not hold")).fetchImpl, request: REQUEST });
  assert.equal(unknown.card, true);
  const none = await loadLiveHistory({ contract, fetchImpl: answering(200, "application/json", envelopeOf("no corpus mounted")).fetchImpl, request: REQUEST });
  assert.equal(none.card, true);
  assert.equal(none.sentence, "This build has no Luxembourg index mounted.");
  const card = renderToStaticMarkup(h(RefusalCard, { code: none.code, sentence: none.sentence, payload: none.payload }));
  assert.equal(view(none), `<section data-answer-state="refusal">${card}</section>`);
  assert.equal(typeof LIVE_HISTORY_REFUSAL_SENTENCES.language_not_available, "string");
});

test("an unreadable answer and a refused request are each a state with a sentence", async () => {
  const broken = structuredClone(envelopeOf(ANSWER));
  broken.result.value.states[0].wording_changed = true;
  const unreadable = historyOutcome({ state: "success", envelope: broken });
  assert.equal(unreadable.state, "invalid_envelope");
  assert.match(unreadable.sentence, /wording_changed is true/);
  const problem = answering(400, "application/problem+json", { type: "about:blank", title: "bad", status: 400, code: "request_schema_invalid" });
  assert.match((await loadLiveHistory({ contract, fetchImpl: problem.fetchImpl, request: REQUEST })).sentence, /refused the provision history request as it was asked/);
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  const session = createHistorySession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ identifier: "x", anchor: " " }), false);
  assert.equal(settled.calls.length, 0);
  const arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes, ["invalid_request", "loading", "success"]);

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.anchor); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createHistorySession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ identifier: "x", anchor: "first" });
  second.ask({ identifier: "x", anchor: "second" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"]);
  assert.deepEqual(later, ["loading", "loading"]);
});
