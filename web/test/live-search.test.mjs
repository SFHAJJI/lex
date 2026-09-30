// The live search screen, measured against envelopes the platform really sent.
//
// The screen asks `search` through the client module when the reader submits, and shows one state.
// These tests drive it with an injected fetch that answers the census envelopes
// (`schemas/v3-platform/envelope-samples.json`: a search answer with hits in both lanes, a
// `language_not_available` refusal and a `no_corpus_mounted` refusal), so every state is the one a
// real server response produces, and the server render is the idle form a browser hydrates.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { LiveSearch, RefusalCard, SearchAnswerView, SearchResultsView, renderLiveSearchPage } from "../.react-build/app.mjs";
import {
  LIVE_SEARCH_IDLE,
  LIVE_SEARCH_REFUSAL_SENTENCES,
  SEARCH_QUERY_MAX,
  SEARCH_TERMS_MAX,
  createSearchSession,
  loadLiveSearch,
  searchOutcome,
  searchParameters,
  searchTerms,
  startLiveSearch,
} from "../scripts/live-search.mjs";
import { REFUSAL_EXAMPLES } from "../scripts/refusal-catalog.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const envelopeOf = (scenarioStart) => {
  const entry = census.envelopes.find((candidate) => candidate.operation === "search" && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds the search envelope "${scenarioStart}..."`);
  return entry.envelope;
};
const ANSWER = "a phrase the held text carries";
const REQUEST = { query: "assemblée générale", language: "fra" };

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

const view = (outcome) => renderToStaticMarkup(h(SearchAnswerView, { outcome }));
const text = (markup) => markup.replace(/<[^>]+>/g, "").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&");

test("the server renders the form in its idle state, and asks nothing", () => {
  const { calls, fetchImpl } = answering(200, "application/json", envelopeOf(ANSWER));
  const markup = renderToStaticMarkup(h(LiveSearch, { contract, fetchImpl }));
  assert.match(markup, /<form role="search"/);
  assert.match(markup, /data-answer-state="idle"/);
  assert.ok(markup.includes(LIVE_SEARCH_IDLE));
  assert.equal(calls.length, 0, "a search is asked when the reader submits, never while rendering");

  const page = renderLiveSearchPage();
  assert.match(page, /<script src="\/client-live-search.js" defer=""><\/script>/);
  assert.match(page, /id="live-search-root"/);
  const form = page.slice(page.indexOf('<form role="search"'), page.indexOf("</form>"));
  assert.ok(form.length > 0);
  assert.doesNotMatch(form, /\sname=/, "no control is named, so a submit the browser performs before hydration sends nothing (review of #772)");
});

test("the request carries the phrase as typed and the language, and nothing else", () => {
  assert.deepEqual(searchParameters(REQUEST), REQUEST);
  assert.deepEqual(searchParameters({ ...REQUEST, query: "  Loyer " }), { query: "  Loyer ", language: "fra" }, "never trimmed or folded: the platform matches bytes");
  assert.deepEqual(searchParameters({ ...REQUEST, after: "strict.a.b" }), { ...REQUEST, after: "strict.a.b" });
  const words = (count) => Array.from({ length: count }, (_, index) => `w${index}`).join(" ");
  assert.equal(searchParameters({ ...REQUEST, query: words(SEARCH_TERMS_MAX) }).query, words(SEARCH_TERMS_MAX));
  assert.ok(searchParameters({ ...REQUEST, query: `${words(SEARCH_TERMS_MAX)} w0` }), "a repeated word is one term, as the platform counts it");
  assert.equal(searchTerms("a\u00a0b\u0085c\u2028d e\t\ta").length, 5, "split where .NET splits: no-break space, NEL, line separator, tabs");
  assert.equal(searchTerms("a\ufeffb").length, 1, "and not where it does not: U+FEFF is not whitespace to the platform");
  for (const [what, request, reason] of [
    ["more distinct words than the platform counts", { ...REQUEST, query: words(SEARCH_TERMS_MAX + 1) }, /at most 32 different words, and this one has 33/],
    ["a blank phrase", { ...REQUEST, query: "   " }, /needs a phrase/],
    ["no phrase", { language: "fra" }, /needs a phrase/],
    ["a phrase over the ceiling", { ...REQUEST, query: "x".repeat(SEARCH_QUERY_MAX + 1) }, /at most 512/],
    ["a language the form does not offer", { ...REQUEST, language: "ltz" }, /not a language this form offers/],
    ["an empty cursor", { ...REQUEST, after: "" }, /cursor the previous page handed over/],
  ]) {
    assert.throws(() => searchParameters(request), reason, what);
  }
  assert.equal(searchParameters({ ...REQUEST, query: "x".repeat(SEARCH_QUERY_MAX) }).query.length, SEARCH_QUERY_MAX);
});

test("a served search answer is the results, read by the search reader", async () => {
  const envelope = envelopeOf(ANSWER);
  const { calls, fetchImpl } = answering(200, "application/json; charset=utf-8", envelope);
  const outcome = await loadLiveSearch({ contract, fetchImpl, request: REQUEST });
  assert.equal(outcome.state, "success");
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/api/v3/search");
  assert.deepEqual(JSON.parse(calls[0].init.body), { operation_id: "search", parameters: REQUEST }, "the body is the operation and the two parameters");

  const markup = view(outcome);
  assert.match(markup, /^<section data-answer-state="success">/);
  const shown = text(markup);
  for (const hit of envelope.result.value.hits) {
    assert.ok(markup.includes(hit.resolve.identifier), `the page pins ${hit.publisher_id} by its permalink`);
    assert.ok(shown.includes(`${hit.publisher_id} in ${hit.work_key}, version of ${hit.applicability_date}`));
  }
  assert.equal((markup.match(/data-lane="strict"/g) ?? []).length, 4);
  assert.equal((markup.match(/data-lane="relaxed"/g) ?? []).length, 1);
  assert.ok(shown.includes("The first hits in the stated order, not the best hits."), "the page says there is no ranker, where the hits are");
  assert.ok(shown.includes("4 with the exact phrase, 1 with every word, in 1 work."));
  assert.ok(shown.includes("This index holds no work titles"), "the work resolution the answer gives is shown");
  assert.doesNotMatch(markup, /Next page/, "a whole page offers no next page");
});

test("a truncated page offers the next page with its own cursor, and a whole-result page does not", async () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  const value = envelope.result.value;
  value.hits = value.hits.slice(0, 2);
  value.limit = 2;
  value.truncated = true;
  const last = value.hits[1];
  value.continue_after = `${last.lane}.${last.state_sha256}.${last.article_identity_sha256}`;
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "success");
  let asked = null;
  const onNextPage = (after) => { asked = after; };
  const markup = renderToStaticMarkup(h(SearchAnswerView, { outcome, onNextPage }));
  assert.match(markup, /<button type="button">Next page<\/button>/);
  assert.equal(asked, null, "rendering asks nothing");

  // The button itself, pressed: it hands over the cursor the page carries, and nothing else (review of #772).
  const find = (node) => {
    if (node === null || typeof node !== "object") return null;
    if (Array.isArray(node)) return node.map(find).find(Boolean) ?? null;
    if (node.type === "button") return node;
    return find(node.props?.children);
  };
  const button = find(SearchResultsView({ view: outcome.view, onNextPage }));
  assert.ok(button, "the truncated page renders a button");
  button.props.onClick();
  assert.equal(asked, value.continue_after, "the next page is asked with this page's own cursor");
});

test("an answer the search reader refuses is an invalid answer, said, never rendered", () => {
  const envelope = structuredClone(envelopeOf(ANSWER));
  envelope.result.value.hits.reverse();
  const outcome = searchOutcome({ state: "success", envelope });
  assert.equal(outcome.state, "invalid_envelope");
  assert.match(outcome.sentence, /relaxed never outranks strict/);
  assert.match(view(outcome), /^<section data-answer-state="invalid_envelope"><p role="status">/);
});

test("the two refusals a search from this page can meet are refusal cards", async () => {
  for (const [scenario, code] of [["no corpus mounted", "no_corpus_mounted"], ["a language the mount holds no text in", "language_not_available"]]) {
    const envelope = envelopeOf(scenario);
    const { fetchImpl } = answering(200, "application/json", envelope);
    const outcome = await loadLiveSearch({ contract, fetchImpl, request: { ...REQUEST, language: code === "language_not_available" ? "deu" : "fra" } });
    assert.equal(outcome.state, "refusal", code);
    assert.equal(outcome.code, code);
    assert.equal(outcome.card, true, `${code}: the card's rules accept the payload the platform sent`);
    assert.equal(outcome.sentence, code === "no_corpus_mounted" ? "This build has no Luxembourg index mounted." : LIVE_SEARCH_REFUSAL_SENTENCES[code]);
    const card = renderToStaticMarkup(h(RefusalCard, { code, sentence: outcome.sentence, payload: outcome.payload }));
    assert.equal(view(outcome), `<section data-answer-state="refusal">${card}</section>`);
  }
  assert.equal(LIVE_SEARCH_REFUSAL_SENTENCES.no_corpus_mounted, undefined, "the missing index is named from the payload, never a sentence claiming none is mounted (review of #775)");
  assert.ok(REFUSAL_EXAMPLES.no_corpus_mounted.payload.required_corpus, "the catalog's example carries the corpus the sentence names");
});

test("a transport failure and an unreadable envelope are each a state with a sentence", async () => {
  const problem = answering(400, "application/problem+json", { type: "about:blank", title: "bad", status: 400, code: "request_schema_invalid" });
  const failed = await loadLiveSearch({ contract, fetchImpl: problem.fetchImpl, request: REQUEST });
  assert.equal(failed.state, "transport_failure");
  assert.match(failed.sentence, /refused the search as it was asked \(request_schema_invalid\); ask it again from the first page/,
    "a server that was reached and refused is not one that could not be reached (review of #772)");
  const offline = await loadLiveSearch({ contract, fetchImpl: async () => { throw new TypeError("failed to fetch"); }, request: REQUEST });
  assert.match(offline.sentence, /could not be reached \(network_error\)/);

  const garbled = answering(200, "application/json", "{");
  const unreadable = await loadLiveSearch({ contract, fetchImpl: garbled.fetchImpl, request: REQUEST });
  assert.equal(unreadable.state, "invalid_envelope");
});

test("a search asks once and hands the state over, and a cancel aborts and silences it", async () => {
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  const outcomes = [];
  let done;
  const arrived = new Promise((resolve) => { done = resolve; });
  startLiveSearch({ contract, fetchImpl: settled.fetchImpl, request: REQUEST, onOutcome: (outcome) => { outcomes.push(outcome); done(); } });
  await arrived;
  assert.equal(settled.calls.length, 1);
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["success"]);

  let aborted = false;
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted = true; reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const silenced = [];
  const cancel = startLiveSearch({ contract, fetchImpl: hanging, request: REQUEST, onOutcome: (outcome) => silenced.push(outcome) });
  cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.equal(aborted, true, "the request is aborted");
  assert.deepEqual(silenced, [], "and nothing is handed over after cancel");
});

test("a session asks when told, says a request it will not send, cancels the search in flight, and asks the next page with the cursor", async () => {
  const outcomes = [];
  const settled = answering(200, "application/json", envelopeOf(ANSWER));
  let done;
  let arrived = new Promise((resolve) => { done = resolve; });
  const session = createSearchSession({ contract, fetchImpl: settled.fetchImpl, onOutcome: (outcome) => { outcomes.push(outcome); if (outcome.state !== "loading") done(); } });

  assert.equal(session.next("strict.a.b"), false, "no next page before a first search");
  assert.equal(session.ask({ ...REQUEST, query: " " }), false);
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["invalid_request"]);
  assert.equal(settled.calls.length, 0, "nothing is asked for a request the form will not send");

  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.ask(REQUEST), true);
  await arrived;
  assert.deepEqual(outcomes.map((outcome) => outcome.state), ["invalid_request", "loading", "success"]);

  arrived = new Promise((resolve) => { done = resolve; });
  assert.equal(session.next("strict.cursor.last"), true);
  await arrived;
  assert.deepEqual(JSON.parse(settled.calls[1].init.body).parameters, { ...REQUEST, after: "strict.cursor.last" }, "the next page repeats the search with the cursor");

  const aborted = [];
  const hanging = async (url, init) => new Promise((resolve, reject) => {
    init.signal.addEventListener("abort", () => { aborted.push(JSON.parse(init.body).parameters.query); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
  });
  const later = [];
  const second = createSearchSession({ contract, fetchImpl: hanging, onOutcome: (outcome) => later.push(outcome.state) });
  second.ask({ ...REQUEST, query: "first" });
  second.ask({ ...REQUEST, query: "second" });
  second.cancel();
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(aborted, ["first", "second"], "a new search cancels the one in flight, and cancel the last");
  assert.deepEqual(later, ["loading", "loading"], "nothing settles after a cancel");
});
