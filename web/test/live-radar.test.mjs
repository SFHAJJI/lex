// The live change radar screen, measured against what the platform really sent.
//
// The screen asks `changes_in_period` through the client module when the reader submits, and shows
// one state. These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: a window holding the first held state, an empty
// window before anything held, and no mount), and render the two-state radar the answer census holds
// through the same view.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { LiveRadar, RadarAnswerView, RefusalCard, renderLiveRadarPage } from "../.react-build/app.mjs";
import { LIVE_RADAR_IDLE, createRadarSession, loadLiveRadar, radarOutcome, radarParameters } from "../scripts/live-radar.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "changes_in_period" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the changes_in_period envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const REQUEST = { dateFrom: "2024-02-01", dateTo: "2024-02-01" };

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

function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]));
  if (node === "<varies-per-run>") { counter.n += 1; return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64); }
  return node;
}

const view = (outcome) => renderToStaticMarkup(h(RadarAnswerView, { outcome }));

test("the server renders the form in its idle state with no named control, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf("a window holding"));
  const markup = renderToStaticMarkup(h(LiveRadar, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_RADAR_IDLE));
  assert.equal(calls.length, 0);
  assert.doesNotMatch(markup.slice(markup.indexOf("<form"), markup.indexOf("</form>")), /\sname=/);
  assert.ok(renderLiveRadarPage().includes('<script src="/client-live-radar.js" defer=""></script>'));
});

test("the request carries the two dates, and an identifier and a language only when given", () => {
  assert.deepEqual(radarParameters(REQUEST), { date_from: "2024-02-01", date_to: "2024-02-01" });
  assert.deepEqual(radarParameters({ ...REQUEST, identifier: "/lu-legilux/x", language: "fra" }), { date_from: "2024-02-01", date_to: "2024-02-01", identifier: "/lu-legilux/x", language: "fra" });
  assert.deepEqual(radarParameters({ ...REQUEST, identifier: "   " }), { date_from: "2024-02-01", date_to: "2024-02-01" }, "a blank identifier is no identifier");
  assert.throws(() => radarParameters({ ...REQUEST, dateTo: "" }), /two calendar dates/);
  assert.throws(() => radarParameters({ ...REQUEST, language: "ltz" }), /not a language this form offers/);
});

test("a window holding a first held state lists it, with the reason it is not compared and the caveat", async () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf("a window holding"));
  const outcome = await loadLiveRadar({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls[0].url, "/api/v3/changes_in_period");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "changes_in_period", parameters: { date_from: "2024-02-01", date_to: "2024-02-01" } });
  const markup = view(outcome);
  assert.ok(markup.includes("2024-02-01 to 2024-02-01: 1 state of 1 work, of 1 held."));
  assert.match(markup, /data-reason="first_held_state"/);
  assert.ok(markup.includes("A version row does not by itself assert a wording change, legal effect, or entry into force."));
});

test("an empty window before anything held says it does not meet what is held", async () => {
  const outcome = await loadLiveRadar({ contract, fetchImpl: answering(200, "application/json", envelopeOf("a window before anything held")).fetchImpl, request: { dateFrom: "1990-01-01", dateTo: "1990-12-31" } });
  assert.equal(outcome.state, "success");
  assert.ok(view(outcome).includes("This window does not meet what this index holds, from 2024-02-01 to 2024-02-01."));
});

test("a two-state radar lists the later state as compared with its baseline", () => {
  const row = answers.sampled.filter((sample) => sample.operation === "changes_in_period")[1];
  const envelope = structuredClone(envelopeOf("a window holding"));
  envelope.result.value = withDigests(row.answer);
  const outcome = radarOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = view(outcome);
  assert.deepEqual([...markup.matchAll(/data-reason="([a-z_]+)"/g)].map((match) => match[1]), ["first_held_state", "changed"]);
  const answer = withDigests(row.answer);
  const [, later] = answer.changes;
  assert.ok(markup.includes(`wording changed from the state of 2024-02-01 (baseline <code>${later.baseline.permalink}</code>): 1 changed, 1 added, 1 removed, 47 unchanged`),
    "the baseline a row was compared with is named by its permalink");
});

test("an uncompared row names the states it could not choose between, or the baseline it would not compare with", () => {
  const row = answers.sampled.filter((sample) => sample.operation === "changes_in_period")[1];
  const shown = (change) => {
    const envelope = structuredClone(envelopeOf("a window holding"));
    envelope.result.value = withDigests(row.answer);
    change(envelope.result.value.changes[1]);
    const outcome = radarOutcome({ state: "success", envelope });
    assert.equal(outcome.state, "success", outcome.sentence);
    return view(outcome);
  };
  const uncompared = (entry, reason) => Object.assign(entry, { reason, wording_changed: null, counts: null, diff: null });

  let other = null;
  let own = null;
  const ambiguous = shown((entry) => {
    own = entry.state.permalink;
    other = `${entry.state.stable_coordinate}--${"e".repeat(64)}`;
    uncompared(entry, "ambiguous_version").candidates = [own, other];
  });
  assert.ok(ambiguous.includes(`(candidates <code>${own}</code>, <code>${other}</code>)`), "an ambiguity lists its candidates");

  let baseline = null;
  const differ = shown((entry) => {
    baseline = entry.baseline.permalink;
    uncompared(entry, "profiles_differ").state.rule_profile_sha256s = ["d".repeat(64)];
  });
  assert.ok(differ.includes(`<code>${baseline}</code>`) && differ.includes("different rule profiles, so they are not compared (baseline"), "profiles that differ name the baseline");
});

test("no mount is the card naming the missing index; an unreadable answer is said as such", async () => {
  const none = await loadLiveRadar({ contract, fetchImpl: answering(200, "application/json", envelopeOf("no corpus mounted")).fetchImpl, request: REQUEST });
  assert.equal(none.card, true);
  assert.equal(none.sentence, "This build has no Luxembourg index mounted.");
  const card = renderToStaticMarkup(h(RefusalCard, { code: none.code, sentence: none.sentence, payload: none.payload }));
  assert.equal(view(none), `<section data-answer-state="refusal">${card}</section>`);
  const broken = structuredClone(envelopeOf("a window holding"));
  broken.result.value.population.versions_in_window = 5;
  const unreadable = radarOutcome({ state: "success", envelope: broken });
  assert.equal(unreadable.state, "invalid_envelope");
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf("a window holding"));
  let done;
  const session = createRadarSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ dateFrom: "x", dateTo: "2024-01-01" }), false);
  assert.equal(settled.calls.length, 0);
  const arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes, ["invalid_request", "loading", "success"]);

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.date_to); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createRadarSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ dateFrom: "2024-01-01", dateTo: "2024-01-02" });
  second.ask({ dateFrom: "2024-01-01", dateTo: "2024-01-03" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["2024-01-02", "2024-01-03"]);
  assert.deepEqual(later, ["loading", "loading"]);
});
