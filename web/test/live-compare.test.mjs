// The live compare screen, measured against what the platform really sent.
//
// The screen asks `diff` through the client module when the reader submits, and shows one state.
// These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: the fixture's same-state comparison and three
// refusals), and render the two-state comparison the answer census holds (one article reworded, one
// id renamed) through the same view.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { CompareAnswerView, LiveCompare, RefusalCard, renderLiveComparePage } from "../.react-build/app.mjs";
import {
  LIVE_COMPARE_IDLE,
  compareOutcome,
  compareParameters,
  createCompareSession,
  loadLiveCompare,
} from "../scripts/live-compare.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const payloads = JSON.parse(await readFile(new URL("../../schemas/v3-platform/refusal-payload-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "diff" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the diff envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "the state on one date against the state on the same date";
const REQUEST = { identifier: "/lu-legilux/loi-1991-08-10-n3", dateFrom: "2024-02-01", dateTo: "2024-02-01", language: "fra" };

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

const view = (outcome) => renderToStaticMarkup(h(CompareAnswerView, { outcome }));

test("the server renders the form in its idle state with no named control, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveCompare, { contract, fetchImpl }));
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_COMPARE_IDLE));
  assert.equal(calls.length, 0);
  assert.doesNotMatch(markup.slice(markup.indexOf("<form"), markup.indexOf("</form>")), /\sname=/);
  assert.ok(renderLiveComparePage().includes('<script src="/client-live-compare.js" defer=""></script>'));
});

test("the request carries the identifier and the two dates as typed, and a language only when chosen", () => {
  assert.deepEqual(compareParameters(REQUEST), { identifier: REQUEST.identifier, date_from: "2024-02-01", date_to: "2024-02-01", language: "fra" });
  assert.deepEqual(compareParameters({ ...REQUEST, language: "" }), { identifier: REQUEST.identifier, date_from: "2024-02-01", date_to: "2024-02-01" });
  assert.throws(() => compareParameters({ ...REQUEST, identifier: " " }), /identifier of a work/);
  assert.throws(() => compareParameters({ ...REQUEST, dateTo: "2024-13-01" }), /two calendar dates/);
  assert.throws(() => compareParameters({ ...REQUEST, language: "ltz" }), /not a language this form offers/);
});

test("the same state on both dates is said as such, with the platform's note", async () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const outcome = await loadLiveCompare({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls[0].url, "/api/v3/diff");
  assert.deepEqual(JSON.parse(calls[0].init.body).parameters, { identifier: REQUEST.identifier, date_from: "2024-02-01", date_to: "2024-02-01", language: "fra" });
  const markup = view(outcome);
  assert.ok(markup.includes("The same version applied on both dates."));
  assert.ok(!markup.includes("data-counts"), "no counts for the same state");
});

test("two states are listed article by article: the moved ones first, the unchanged one disclosure away", () => {
  const row = answers.sampled.filter((sample) => sample.operation === "diff")[1];
  const envelope = structuredClone(envelopeOf(ANSWER));
  envelope.result.value = withDigests(row.answer);
  const outcome = compareOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = view(outcome);
  assert.ok(markup.includes("1 changed, 1 added, 1 removed, 47 unchanged."));
  const moved = markup.slice(markup.indexOf("data-moved"), markup.indexOf("</ol>", markup.indexOf("data-moved")));
  assert.deepEqual([...moved.matchAll(/data-status="([a-z]+)"><strong>([^<]+)</g)].map((match) => [match[2], match[1]]),
    [["art_15", "changed"], ["art_16", "removed"], ["art_16-new", "added"]]);
  assert.match(markup, /<details><summary>47 unchanged articles<\/summary>/);
  assert.ok(markup.includes("nothing about legal effect is asserted"));
  assert.ok(!/color|colour/i.test(markup), "each status is a word, not a colour");

  // Review of #783: the next state's date is its start, not this one's end; each side's article date
  // conflicts are counted, with the platform's rule; and the wording digests are whole.
  const [comparison] = row.answer.comparisons;
  const sideText = (label) => markup.slice(markup.indexOf(`data-side="${label}"`), markup.indexOf("</p>", markup.indexOf(`data-side="${label}"`))).replace(/<[^>]+>/g, "").replaceAll("&#x27;", "'");
  assert.ok(sideText("From").includes("the state applying from 2024-02-01 (the next state held applies from 2025-03-07)"), sideText("From"));
  assert.ok(!markup.includes(" until "), "no side is said to apply until the next state's date");
  assert.ok(sideText("From").includes(`${comparison.from.validity_conflict_count} with their own date differing from the state's`));
  assert.ok(sideText("To").includes(`${comparison.to.validity_conflict_count} with their own date differing from the state's`));
  assert.equal(comparison.from.validity_conflict_count, 49, "the sample's first state holds the conflicts the page must not hide");
  assert.ok(markup.replaceAll("&#x27;", "'").includes("Article_valid_from is the publisher's article-level applicability date"), "the platform's validity conflict rule is said");
  const changed = comparison.articles.find((entry) => entry.publisher_id === "art_15");
  assert.ok(markup.includes(`<code>${changed.from[0].wording_sha256}</code> → <code>${changed.to[0].wording_sha256}</code>`), "the whole wording digests of art_15");
});

test("the refusals a comparison from this page can meet", async () => {
  const early = await loadLiveCompare({ contract, fetchImpl: answering(200, "application/json", envelopeOf("a from date before the work's history")).fetchImpl, request: REQUEST });
  assert.equal(early.code, "no_version_for_date");
  assert.equal(early.card, false);
  assert.match(early.sentence, /begins on 2024-02-01\.$/);
  const unknown = await loadLiveCompare({ contract, fetchImpl: answering(200, "application/json", envelopeOf("a work the index does not hold")).fetchImpl, request: REQUEST });
  assert.equal(unknown.card, false);
  const none = await loadLiveCompare({ contract, fetchImpl: answering(200, "application/json", envelopeOf("no corpus mounted")).fetchImpl, request: REQUEST });
  assert.equal(none.card, true);
  assert.equal(none.sentence, "This build has no Luxembourg index mounted.");

  for (const code of ["ambiguous_version", "profiles_differ", "language_not_available"]) {
    const row = payloads.produced.find((entry) => entry.code === code);
    const envelope = structuredClone(envelopeOf("no corpus mounted"));
    envelope.refusal.code = code;
    envelope.refusal.helpful_payload = row.payload;
    const outcome = compareOutcome({ state: "refusal", envelope });
    assert.equal(outcome.card, true, `${code}: the card's rules accept the platform's payload`);
    assert.doesNotThrow(() => view(outcome), code);
    const card = renderToStaticMarkup(h(RefusalCard, { code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`, code);
  }
});

test("a session asks when told, says a request it will not send, and a new request cancels the one in flight", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  const session = createCompareSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome.state); if (outcome.state !== "loading") done?.(); } });
  assert.equal(session.ask({ ...REQUEST, dateFrom: "yesterday" }), false);
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
  const second = createCompareSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ ...REQUEST, identifier: "first" });
  second.ask({ ...REQUEST, identifier: "second" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"]);
  assert.deepEqual(later, ["loading", "loading"]);
});
