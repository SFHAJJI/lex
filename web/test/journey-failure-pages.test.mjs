// The journeys' runs of R60's pages (the 404 page and a failure's retry), held on observations built to pass and on each
// way one can fail. The browser runs themselves need a local API and a browser; what they conclude is held here.

import assert from "node:assert/strict";
import test from "node:test";

import {
  FAILURE_LINE,
  JOURNEY_STEPS,
  NOT_FOUND_SAID,
  NOT_FOUND_STEP,
  RETRIED_SEARCH_STEP,
  bodyOn,
  failurePageExpectations,
  journeyVerdict,
  todayInLuxembourgAt,
} from "../scripts/journey.mjs";
import { cspValue } from "../scripts/csp.mjs";
import { liveChrome } from "../scripts/live-chrome.mjs";

const ORIGIN = "http://127.0.0.1:5000";

/** Today in Luxembourg, which a home page's search (and the 404 page's, which is the home page's) sends. */
const TODAY = todayInLuxembourgAt(Date.now());

/** A run of `step` that did what the page must do: its document, its script, and `asks` requests to the API. */
function observedAt(step, { pageStatus = 404, asks = 1, answerState = "success", text = "", retried } = {}) {
  return {
    answerState,
    text,
    requests: [
      { url: `${ORIGIN}${step.path}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-search.js`, method: "GET", headers: {} },
      ...Array.from({ length: asks }, () => ({
        url: `${ORIGIN}/api/v3/${step.operation}`,
        method: "POST",
        headers: { "Content-Type": "application/json" },
        headersSent: true,
        postData: JSON.stringify(bodyOn(step.body, TODAY)),
      })),
    ],
    todays: [TODAY],
    console: [],
    storage: { local: 0, session: 0 },
    csp: cspValue(),
    hydrated: "clean",
    location: `${ORIGIN}${step.path}`,
    history: { atLoad: 2, atEnd: 2, state: "null" },
    cookie: "",
    pageStatus,
    retried,
  };
}

test("R60's runs: the 404 page searched with the fixture mount and with none, and the search with no mount retried", () => {
  const runs = failurePageExpectations();
  assert.deepEqual(runs.map(([name, , mounted]) => [name, mounted]), [
    ["not found, with the fixture mount", true],
    ["not found, with no mount", false],
    ["search retried, with no mount", false],
  ]);
  assert.equal(NOT_FOUND_STEP.path, "/en/no-such-page");
  assert.equal(NOT_FOUND_STEP.notFound, true);
  assert.deepEqual(NOT_FOUND_STEP.body, JOURNEY_STEPS.home.body, "the 404 page's search asks what the home page's asks");
  assert.equal(NOT_FOUND_STEP.frontDoor, false, "the 404 page offers the home page's search, not its front door");
  assert.equal(NOT_FOUND_SAID, liveChrome("en").notFound.heading, "the 404 page's own heading");
  assert.equal(FAILURE_LINE, liveChrome("en").failure.line, "the failure panel's own line");
  assert.deepEqual(RETRIED_SEARCH_STEP.body, JOURNEY_STEPS.search.body);
  for (const [, expected] of runs.slice(1)) {
    assert.equal(expected.refusalCode, "no_corpus_mounted");
    assert.ok(expected.texts.includes(FAILURE_LINE), "with no mount, the page says the failure line");
  }
});

test("the 404 page's run passes with status 404 and fails with any other", () => {
  const expected = { origin: ORIGIN, step: NOT_FOUND_STEP, state: "success", texts: [NOT_FOUND_SAID] };
  assert.deepEqual(journeyVerdict(observedAt(NOT_FOUND_STEP, { text: NOT_FOUND_SAID }), expected), []);
  for (const pageStatus of [200, 302, null]) {
    const failures = journeyVerdict(observedAt(NOT_FOUND_STEP, { text: NOT_FOUND_SAID, pageStatus }), expected);
    assert.ok(failures.includes(`the page at /en/no-such-page arrived with status ${pageStatus ?? "not observed"}, not 404`), failures.join(" | "));
  }
  // Any other step is not held to a status.
  const search = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  assert.deepEqual(journeyVerdict(observedAt(JOURNEY_STEPS.search, { pageStatus: 200 }), search), []);
});

test("a retried run passes only when the retry was pressed and asked the very same request once more", () => {
  const expected = { origin: ORIGIN, step: RETRIED_SEARCH_STEP, state: "refusal", refusalCode: "no_corpus_mounted", texts: [FAILURE_LINE] };
  const text = `${FAILURE_LINE} no_corpus_mounted`;
  const passing = observedAt(RETRIED_SEARCH_STEP, { pageStatus: 200, asks: 2, answerState: "refusal", text, retried: "reached" });
  assert.deepEqual(journeyVerdict(passing, expected), []);

  const once = journeyVerdict({ ...passing, requests: passing.requests.slice(0, 3) }, expected);
  assert.ok(once.includes("the page made 1 requests to the API, not exactly two"), once.join(" | "));
  for (const retried of ["absent", "pressed", undefined]) {
    const failures = journeyVerdict({ ...passing, retried }, expected);
    assert.ok(failures.includes(`the failure panel's retry was not pressed and settled (${retried ?? "not observed"})`), failures.join(" | "));
  }
  const another = { ...passing, requests: [...passing.requests.slice(0, 3), { ...passing.requests[3], postData: JSON.stringify({ operation_id: "search", parameters: { query: "x", language: "fra" } }) }] };
  assert.ok(journeyVerdict(another, expected).some((failure) => /body was .*"query":"x"/.test(failure)), "the retry asks the very same request");
  assert.ok(journeyVerdict({ ...passing, text: "no_corpus_mounted" }, expected).includes(`the page does not show "${FAILURE_LINE}"`), "the failure line is said");
});
