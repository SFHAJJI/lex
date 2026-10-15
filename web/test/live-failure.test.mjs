// A service failure on a live screen, panel by panel (the specification of record's R60; plan item 6.9).
//
// Every screen (search, dossier, reading, provision history, compare, radar, export, Trust and Coverage) is held to the
// same rule, through its own outcome reader and view:
// - a request that did not complete (the network, a transport problem the API or the live server sent, a 5xx without a
//   problem body) and an operational refusal (rate_limited, upstream_unreachable, no_corpus_mounted, snapshot_unknown,
//   rendered from its payload) are shown inside the failure panel: the specification's line first, then what the screen
//   says of it, a retry, and the publishers' official sites;
// - the panel never shows an empty list, a zero or "no version";
// - an answer, a refusal about the law and a request the form would not send are not failures, and show no panel;
// - the retry asks exactly the request that failed, again, and nothing else.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
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
import { FAILURE_STATES, OFFICIAL_SITES, OPERATIONAL_REFUSALS, isFailure } from "../scripts/live-failure.mjs";
import { REFUSAL_CODES } from "../scripts/refusal-card.mjs";
import { publisherSourceUri } from "../scripts/routes.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";
import { createSearchSession, searchOutcome } from "../scripts/live-search.mjs";
import { createDossierSession, dossierOutcome } from "../scripts/live-dossier.mjs";
import { createReadingSession, readingOutcome } from "../scripts/live-reading.mjs";
import { createHistorySession, historyOutcome } from "../scripts/live-history.mjs";
import { compareOutcome, createCompareSession } from "../scripts/live-compare.mjs";
import { createRadarSession, radarOutcome } from "../scripts/live-radar.mjs";
import { coverageOutcome, startLiveCoverage } from "../scripts/live-coverage.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const copy = liveChrome("en").failure;

/** Each screen: its name, the operation it asks, its outcome reader and its view. */
const SCREENS = [
  ["search", "search", searchOutcome, SearchAnswerView],
  ["dossier", "dossier", dossierOutcome, DossierAnswerView],
  ["reading", "evidence_bundle", readingOutcome, ReadingAnswerView],
  ["history", "article_history", historyOutcome, HistoryAnswerView],
  ["compare", "diff", compareOutcome, CompareAnswerView],
  ["radar", "changes_in_period", radarOutcome, RadarAnswerView],
  ["export", "evidence_bundle", readingOutcome, ExportAnswerView],
  ["coverage", "coverage", coverageOutcome, CoverageAnswerView],
];

/** Each operational refusal with the payload the registry names for it (`V3OperationRegistry`). */
const OPERATIONAL_PAYLOADS = Object.freeze({
  rate_limited: { retry_after: 30, retryable: true },
  upstream_unreachable: { upstream: "lu-legilux", retryable: true },
  no_corpus_mounted: { required_corpus: "lu" },
  snapshot_unknown: { snapshot_id: "f".repeat(64), what_would_answer: "a cursor from this log" },
});

/** What reached a screen that did not complete, as `askV3` hands it over. */
const NOT_COMPLETED = [
  { state: "transport_failure", code: "network_error" },
  // The live server's own problems when the API cannot be reached or answers too late, and the API's for an internal failure.
  { state: "transport_failure", code: "api_unreachable", status: 502 },
  { state: "transport_failure", code: "api_timeout", status: 504 },
  { state: "transport_failure", code: "internal_failure", status: 500 },
  // A 5xx with no problem body (a gateway's page), which the client cannot read as an envelope.
  { state: "invalid_envelope", reason: "the API answered 503 text/html, not an envelope" },
];

const props = (outcome) => ({ outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {}, onRetry: () => {} });
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/\s+/g, " ");

/** What a failure panel must show, and must never show. */
function assertFailurePanel(label, markup, said) {
  assert.match(markup, /<div class="failure-panel" data-failure="[a-z_]+">/, `${label}: the failure panel`);
  const line = markup.indexOf(copy.line.replace(/'/g, "&#x27;"));
  assert.ok(line > 0, `${label}: the specification's line`);
  assert.match(markup.slice(markup.indexOf('<div class="failure-panel"')), /^<div [^>]*><p class="failure-line" data-failure-line=""><strong>/, `${label}: the line comes first in the panel`);
  if (said !== undefined) {
    const at = text(markup).indexOf(said);
    assert.ok(at > text(markup).indexOf(copy.line), `${label}: the screen's own sentence, kept, below the line: ${said}`);
  }
  assert.match(markup, /<button type="button" class="failure-retry" data-retry="">Try again<\/button>/, `${label}: a retry`);
  for (const site of OFFICIAL_SITES) {
    assert.ok(markup.includes(`<a href="${site.uri}" rel="external" data-publisher="${site.publisher}">${copy.sites[site.key]}</a>`), `${label}: ${site.uri}`);
  }
  // Never an empty list, a zero or "no version" (R60): no hit list, no row list, no counted population, no count of zero.
  for (const answerOnly of ["data-no-hit", "data-no-row", "data-population", 'class="hits"', "data-export-state"]) {
    assert.ok(!markup.includes(answerOnly), `${label}: no ${answerOnly}`);
  }
  assert.doesNotMatch(text(markup), /(?<![\w-])0(?![\w-])/, `${label}: no zero`);
  assert.doesNotMatch(text(markup), /no version|no held state|none held/i, `${label}: no "no version"`);
  // The panel's lists are the official sites' alone.
  assert.equal((markup.match(/<ul/g) ?? []).length - (markup.match(/<ul class="refusal-/g) ?? []).length, 1, `${label}: one list, the official sites`);
}

test("the failure states are the requests that did not complete and the operational refusals, the registry's own codes", () => {
  assert.deepEqual([...FAILURE_STATES], ["transport_failure", "invalid_envelope"]);
  assert.deepEqual([...OPERATIONAL_REFUSALS].sort(), ["no_corpus_mounted", "rate_limited", "snapshot_unknown", "upstream_unreachable"]);
  for (const code of OPERATIONAL_REFUSALS) assert.ok(REFUSAL_CODES.includes(code), `${code} is a registry code`);
  for (const code of REFUSAL_CODES) {
    assert.equal(isFailure({ state: "refusal", code }), OPERATIONAL_REFUSALS.includes(code), `${code}: ${OPERATIONAL_REFUSALS.includes(code) ? "about this service" : "an answer about the law"}`);
  }
  for (const state of ["success", "idle", "loading", "invalid_request"]) assert.equal(isFailure({ state }), false, state);
  for (const state of FAILURE_STATES) assert.equal(isFailure({ state }), true, state);
  assert.equal(isFailure(null), false);
});

test("the official sites are the publishers' own, each a link the publisher-link policy admits", () => {
  assert.deepEqual(OFFICIAL_SITES.map((site) => site.publisher), ["lu-legilux", "eu-eurlex"]);
  for (const site of OFFICIAL_SITES) {
    assert.equal(publisherSourceUri({ publisher: site.publisher, uri: site.uri }), site.uri, site.uri);
    assert.ok(Object.hasOwn(copy.sites, site.key), `${site.key} is named in the table`);
    assert.ok(Object.hasOwn(liveChrome("fr").failure.sites, site.key), `${site.key} is named in French`);
  }
});

test("on every screen, a request that did not complete is shown in the failure panel, under the line, with the screen's own sentence", () => {
  for (const [name, , outcomeOf, View] of SCREENS) {
    for (const asked of NOT_COMPLETED) {
      const outcome = outcomeOf(asked);
      assert.equal(outcome.state, asked.state, `${name}: ${asked.code ?? asked.reason}`);
      const markup = renderToStaticMarkup(h(View, props(outcome)));
      assert.match(markup, new RegExp(`^<section data-answer-state="${asked.state}">`), `${name}: the state stays marked`);
      assertFailurePanel(`${name}, ${asked.code ?? asked.reason}`, markup, outcome.sentence);
    }
  }
});

test("on every screen, an operational refusal renders from its payload, with the same line", () => {
  for (const [name, , outcomeOf, View] of SCREENS) {
    for (const code of OPERATIONAL_REFUSALS) {
      const envelope = { refusal: { code, helpful_payload: OPERATIONAL_PAYLOADS[code] }, context: { publisher: "lu-legilux" } };
      const outcome = outcomeOf({ state: "refusal", envelope });
      assert.equal(outcome.state, "refusal", `${name}: ${code}`);
      const markup = renderToStaticMarkup(h(View, props(outcome)));
      assert.match(markup, /^<section data-answer-state="refusal">/, `${name}: ${code} stays a refusal`);
      assertFailurePanel(`${name}, ${code}`, markup);
      assert.ok(markup.includes(`data-failure="${code}"`), `${name}: the panel names ${code}`);
      // The card, from the payload: the code, and each payload member under its own name.
      assert.ok(markup.includes(`<code class="refusal-code">${code}</code>`), `${name}: ${code}'s card`);
      for (const member of Object.keys(OPERATIONAL_PAYLOADS[code])) assert.ok(markup.includes(`<dt>${member}</dt>`), `${name}: ${code} shows ${member}`);
      assert.ok(markup.indexOf("data-failure-line") < markup.indexOf("refusal-card"), `${name}: ${code}: the line above the card`);
    }
  }
});

test("the census's own no_corpus_mounted refusals, on each screen that meets them, are failures; its refusals about the law and its answers are not", () => {
  let operational = 0;
  let aboutTheLaw = 0;
  let answers = 0;
  for (const [name, operation, outcomeOf, View] of SCREENS) {
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation)) {
      const outcome = outcomeOf(entry.envelope.refusal ? { state: "refusal", envelope: entry.envelope } : { state: "success", envelope: entry.envelope });
      const markup = renderToStaticMarkup(h(View, props(outcome)));
      if (entry.envelope.refusal && OPERATIONAL_REFUSALS.includes(entry.envelope.refusal.code)) {
        operational += 1;
        assertFailurePanel(`${name}: ${entry.scenario}`, markup);
      } else {
        if (entry.envelope.refusal) aboutTheLaw += 1;
        else answers += 1;
        assert.ok(!markup.includes("failure-panel") && !markup.includes(copy.line.replace(/'/g, "&#x27;")), `${name}: ${entry.scenario} is no failure`);
      }
    }
  }
  assert.ok(operational >= 7, `each screen met a census no_corpus_mounted (${operational})`);
  assert.ok(aboutTheLaw >= 5 && answers >= 8, `refusals about the law (${aboutTheLaw}) and answers (${answers}) were rendered too`);
});

test("a request the form would not send, an idle screen and a loading one show no failure panel", () => {
  for (const [name, , , View] of SCREENS) {
    for (const outcome of [{ state: "invalid_request", sentence: "a search needs a phrase to look for." }, { state: "loading", sentence: "Asking this server." }]) {
      if (name === "export" && outcome.state !== "invalid_request") continue;
      const markup = renderToStaticMarkup(h(View, props(outcome)));
      assert.ok(!markup.includes("failure-panel"), `${name}: ${outcome.state}`);
    }
  }
});

/** A fetch that answers each call with `answers[i]` (the last one repeated): a thrown error, or `[status, type, body]`. */
function fetching(...answers) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, body: init.body });
    const answer = answers[Math.min(calls.length - 1, answers.length - 1)];
    if (answer instanceof Error) throw answer;
    const [status, type, body] = answer;
    return {
      status,
      headers: { get: (name) => (name.toLowerCase() === "content-type" ? type : null) },
      text: async () => body,
    };
  };
  return { calls, fetchImpl };
}

/** Waits until `outcomes` holds `count` states. */
async function settled(outcomes, count) {
  for (let tries = 0; tries < 200 && outcomes.length < count; tries += 1) await new Promise((resolve) => setTimeout(resolve, 5));
  assert.equal(outcomes.length, count, `${count} states reached`);
}

/** Each session, a request it sends, and the operation it asks. */
const SESSIONS = [
  ["search", createSearchSession, { query: "assemblée générale", language: "fra" }, "search"],
  ["dossier", createDossierSession, { identifier: "/lu-legilux/loi-1991-08-10-n3", language: "" }, "dossier"],
  ["reading", createReadingSession, { identifier: "/lu-legilux/loi-1991-08-10-n3", date: "2024-02-01", language: "" }, "evidence_bundle"],
  ["history", createHistorySession, { identifier: "/lu-legilux/loi-1991-08-10-n3", anchor: "art_15", language: "" }, "article_history"],
  ["compare", createCompareSession, { identifier: "/lu-legilux/loi-1991-08-10-n3", dateFrom: "2024-02-01", dateTo: "2024-02-01", language: "" }, "diff"],
  ["radar", createRadarSession, { dateFrom: "2024-02-01", dateTo: "2024-02-01", identifier: "", language: "" }, "changes_in_period"],
];

test("through the client, a network failure, a 5xx problem and a 5xx page each end in a failure, and the retry asks the very same request again", async () => {
  const network = Object.assign(new TypeError("Failed to fetch"), {});
  const problem = [502, "application/problem+json", JSON.stringify({ schema: "lex-v3-live-server-problem/1", code: "api_unreachable" })];
  const page = [503, "text/html", "<!doctype html><title>Service Unavailable</title>"];
  for (const [name, create, request, operation] of SESSIONS) {
    for (const [label, answer, state] of [["network", network, "transport_failure"], ["502 problem", problem, "transport_failure"], ["503 page", page, "invalid_envelope"]]) {
      const { calls, fetchImpl } = fetching(answer);
      const outcomes = [];
      const session = create({ contract, fetchImpl, onOutcome: (outcome) => outcomes.push(outcome) });
      assert.equal(session.retry(), false, `${name}: nothing to retry before a request`);
      assert.equal(session.ask(request), true);
      await settled(outcomes, 2);
      assert.equal(outcomes[1].state, state, `${name}, ${label}`);
      assert.ok(isFailure(outcomes[1]), `${name}, ${label}: a failure`);
      assert.equal(session.retry(), true, `${name}, ${label}: retried`);
      await settled(outcomes, 4);
      assert.deepEqual(outcomes.map((outcome) => outcome.state), ["loading", state, "loading", state], `${name}, ${label}`);
      assert.equal(calls.length, 2, `${name}, ${label}: asked twice, once by the reader's retry`);
      assert.equal(calls[1].url, `/api/v3/${operation}`);
      assert.equal(calls[1].body, calls[0].body, `${name}, ${label}: the very same request`);
    }
  }
});

test("Trust and Coverage's one request, failed on load, is shown in the failure panel (its retry asks it again, `LiveCoverage`)", async () => {
  const { calls, fetchImpl } = fetching(new TypeError("Failed to fetch"));
  const outcomes = [];
  startLiveCoverage({ contract, fetchImpl, onOutcome: (outcome) => outcomes.push(outcome) });
  await settled(outcomes, 1);
  assert.equal(outcomes[0].state, "transport_failure");
  assert.ok(isFailure(outcomes[0]));
  const markup = renderToStaticMarkup(h(CoverageAnswerView, props(outcomes[0])));
  assertFailurePanel("coverage", markup, outcomes[0].sentence);
  assert.equal(calls.length, 1);
});

test("the French failure panel says the reviewed line and the official sites in French", async () => {
  const french = liveChrome("fr").failure;
  assert.equal(french.line, "Lex n’a pas pu mener à bien cette demande. Ce n’est pas une réponse sur le droit.");
  assert.equal(french.retry, "Réessayer");
  assert.equal(french.official, "Les sites officiels des éditeurs :", "a no-break space before the colon");
  assert.deepEqual({ ...french.sites }, { legilux: "Legilux, pour le droit luxembourgeois", eurlex: "EUR-Lex, pour le droit de l’UE" });
});
