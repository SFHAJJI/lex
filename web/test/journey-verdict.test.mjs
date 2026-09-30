// The journey's verdict, held on observations built to pass and on each way one can fail.
//
// The browser run itself needs a local API and a browser and is not part of this suite; what it
// concludes from what it observed is, so a verdict that let a failing page pass cannot land unnoticed.

import assert from "node:assert/strict";
import test from "node:test";

import { DOSSIER_IDENTIFIER, HISTORY_ANCHOR, JOURNEY_STEPS, READING_DATE, SEARCH_PHRASE, journeyVerdict } from "../scripts/journey.mjs";
import { cspValue } from "../scripts/csp.mjs";

const ORIGIN = "http://127.0.0.1:5000";
const CORPUS = "a".repeat(64);
const INDEX = "b".repeat(64);

function good() {
  return {
    answerState: "success",
    text: `Coverage ${CORPUS} ${INDEX}`,
    requests: [
      { url: `${ORIGIN}/`, method: "GET", headers: {} },
      { url: `${ORIGIN}/client-live.js`, method: "GET", headers: {} },
      { url: `${ORIGIN}/api/v3/coverage`, method: "POST", headers: { "Content-Type": "application/json" }, headersSent: true },
    ],
    console: [],
    storage: { local: 0, session: 0 },
    csp: cspValue(),
    hydrated: "clean",
  };
}

const SUCCESS = { origin: ORIGIN, state: "success", corpusSha256: CORPUS, indexSha256: INDEX };

test("a run that did what the page must do passes", () => {
  assert.deepEqual(journeyVerdict(good(), SUCCESS), []);
  const refused = { ...good(), answerState: "refusal", text: "no_corpus_mounted This build has no index mounted." };
  assert.deepEqual(journeyVerdict(refused, { origin: ORIGIN, state: "refusal", refusalCode: "no_corpus_mounted" }), []);
});

test("the empty provisional referrer a browser reports under no-referrer is no referrer", () => {
  const observed = good();
  observed.requests[2].headers.Referer = "";
  assert.deepEqual(journeyVerdict(observed, SUCCESS), []);
});

test("each way a run can fail is a failure, named", () => {
  const cases = [
    ["another state", (o) => { o.answerState = "loading"; }, /ended in loading/],
    ["a digest missing", (o) => { o.text = `Coverage ${CORPUS}`; }, /mounted digest b{64}/],
    ["no API request", (o) => { o.requests = o.requests.slice(0, 2); }, /0 requests to the API/],
    ["two API requests", (o) => { o.requests.push({ ...o.requests[2] }); }, /2 requests to the API/],
    ["another operation", (o) => { o.requests[2].url = `${ORIGIN}/api/v3/search`; }, /not POST \/api\/v3\/coverage/],
    ["a query string", (o) => { o.requests[2].url = `${ORIGIN}/api/v3/coverage?q=loyer`; }, /not POST \/api\/v3\/coverage/],
    ["a GET to the API", (o) => { o.requests[2].method = "GET"; }, /used GET/],
    ["a referrer", (o) => { o.requests[2].headers.Referer = `${ORIGIN}/`; }, /carried a referrer/],
    ["a cookie", (o) => { o.requests[2].headers.Cookie = "s=1"; }, /carried a cookie/],
    ["only provisional headers", (o) => { o.requests[2].headersSent = false; }, /were not observed/],
    ["another origin", (o) => { o.requests.push({ url: "https://tracker.invalid/p.gif", method: "GET", headers: {} }); }, /another origin/],
    ["an API request elsewhere", (o) => { o.requests[2].url = "http://127.0.0.1:6000/api/v3/coverage"; }, /left the page's origin/],
    ["a console line", (o) => { o.console = ["error: boom"]; }, /logged 1 console/],
    ["storage written", (o) => { o.storage.local = 1; }, /wrote to storage/],
    ["another CSP", (o) => { o.csp = "default-src *"; }, /CSP is not the reviewed one/],
    ["a recovered hydration", (o) => { o.hydrated = "recovered"; }, /hydration was recovered/],
    ["hydration never reported", (o) => { o.hydrated = null; }, /never reported/],
  ];
  for (const [what, mutate, reason] of cases) {
    const observed = good();
    mutate(observed);
    const failures = journeyVerdict(observed, SUCCESS);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
  const refused = { ...good(), answerState: "refusal", text: "a refusal" };
  assert.ok(journeyVerdict(refused, { origin: ORIGIN, state: "refusal", refusalCode: "no_corpus_mounted" })
    .some((failure) => /does not name the refusal no_corpus_mounted/.test(failure)));
});

function goodSearch() {
  return {
    ...good(),
    text: `“${SEARCH_PHRASE}” in fra: 4 with the exact phrase, 1 with every word, in 1 work. art_15 in x`,
    requests: [
      { url: `${ORIGIN}/search.html`, method: "GET", headers: {} },
      { url: `${ORIGIN}/client-live-search.js`, method: "GET", headers: {} },
      {
        url: `${ORIGIN}/api/v3/search`,
        method: "POST",
        headers: { "Content-Type": "application/json" },
        headersSent: true,
        postData: JSON.stringify(JOURNEY_STEPS.search.body),
      },
    ],
    location: `${ORIGIN}/search.html`,
    history: { atLoad: 2, atEnd: 2, state: "null" },
    cookie: "",
  };
}

const SEARCH = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success", texts: ["4 with the exact phrase", "art_15 in"] };

test("a search run that typed, submitted and read the answer passes", () => {
  assert.deepEqual(journeyVerdict(goodSearch(), SEARCH), []);
});

test("each way a search run can fail is a failure, named", () => {
  const cases = [
    ["another body", (o) => { o.requests[2].postData = JSON.stringify({ operation_id: "search", parameters: { query: SEARCH_PHRASE, language: "fra", echo: 1 } }); }, /body was .*echo/],
    ["no body observed", (o) => { delete o.requests[2].postData; }, /body was not observed/],
    ["the phrase in the address", (o) => { o.location = `${ORIGIN}/search.html?query=x`; }, /ended at .*\?query=x/],
    ["the coverage operation", (o) => { o.requests[2].url = `${ORIGIN}/api/v3/coverage`; }, /not POST \/api\/v3\/search/],
    ["a text missing", (o) => { o.text = "nothing"; }, /does not show "4 with the exact phrase"/],
    ["two searches", (o) => { o.requests.push({ ...o.requests[2] }); }, /2 requests to the API/],
    ["a history entry pushed", (o) => { o.history.atEnd = 3; }, /changed the history from 2 entries to 3/],
    ["history state written", (o) => { o.history.state = JSON.stringify({ query: SEARCH_PHRASE }); }, /wrote history state/],
    ["a cookie set", (o) => { o.cookie = "lastq=x"; }, /set a cookie: lastq=x/],
  ];
  for (const [what, mutate, reason] of cases) {
    const observed = goodSearch();
    mutate(observed);
    const failures = journeyVerdict(observed, SEARCH);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
});

test("a coverage run is still judged as one, with no body required", () => {
  assert.equal(JOURNEY_STEPS.coverage.body, null);
  assert.deepEqual(journeyVerdict({ ...good(), location: `${ORIGIN}/` }, SUCCESS), []);
  assert.ok(journeyVerdict({ ...good(), location: `${ORIGIN}/?x=1` }, SUCCESS).some((failure) => /ended at/.test(failure)));
});

test("a dossier run is held to its own page, operation and exact body", () => {
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/dossier.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/dossier`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.dossier.body) },
  ];
  observed.location = `${ORIGIN}/dossier.html`;
  observed.text = "loi-1991-08-10-n3 1 state, from 2024-02-01 to 2024-02-01.";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.dossier, state: "success", texts: ["1 state, from"] };
  assert.deepEqual(JOURNEY_STEPS.dossier.body, { operation_id: "dossier", parameters: { identifier: DOSSIER_IDENTIFIER } }, "any held language: no language sent");
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "dossier", parameters: { identifier: DOSSIER_IDENTIFIER, language: "fra" } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was .*"language":"fra"/.test(failure)));
});

test("a reading run types two fields and is held to the exact body of both", () => {
  assert.deepEqual(JOURNEY_STEPS.reading.typed, [DOSSIER_IDENTIFIER, READING_DATE]);
  assert.deepEqual(JOURNEY_STEPS.reading.body, { operation_id: "evidence_bundle", parameters: { identifier: DOSSIER_IDENTIFIER, date: READING_DATE } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/reading.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.reading.body) },
  ];
  observed.location = `${ORIGIN}/reading.html`;
  observed.text = "49 articles quoted";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.reading, state: "success", texts: ["49 articles quoted"] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "evidence_bundle", parameters: { identifier: DOSSIER_IDENTIFIER } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was/.test(failure)), "a request that dropped the date fails");
});

test("a provision history run types the identifier and the article id and is held to both in the body", () => {
  assert.deepEqual(JOURNEY_STEPS.history.typed, [DOSSIER_IDENTIFIER, HISTORY_ANCHOR]);
  assert.deepEqual(JOURNEY_STEPS.history.body, { operation_id: "article_history", parameters: { identifier: DOSSIER_IDENTIFIER, anchor: HISTORY_ANCHOR } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/history.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/article_history`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.history.body) },
  ];
  observed.location = `${ORIGIN}/history.html`;
  observed.text = "art_15 in loi-1991-08-10-n3";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.history, state: "success", texts: ["art_15 in"] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].url = `${ORIGIN}/api/v3/evidence_bundle`;
  assert.ok(journeyVerdict(observed, expected).some((failure) => /not POST \/api\/v3\/article_history/.test(failure)));
});

test("a compare run types the identifier and two dates and is held to all three in the body", () => {
  assert.deepEqual(JOURNEY_STEPS.compare.typed, [DOSSIER_IDENTIFIER, READING_DATE, READING_DATE]);
  assert.deepEqual(JOURNEY_STEPS.compare.body, { operation_id: "diff", parameters: { identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: READING_DATE } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/compare.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/diff`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.compare.body) },
  ];
  observed.location = `${ORIGIN}/compare.html`;
  observed.text = "The same version applied on both dates.";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.compare, state: "success", texts: ["The same version applied on both dates."] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "diff", parameters: { identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: "2025-01-01" } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was/.test(failure)), "a request with another date fails");
});

test("a radar run types two dates and is held to exactly those in the body", () => {
  assert.deepEqual(JOURNEY_STEPS.radar.typed, [READING_DATE, READING_DATE]);
  assert.deepEqual(JOURNEY_STEPS.radar.body, { operation_id: "changes_in_period", parameters: { date_from: READING_DATE, date_to: READING_DATE } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/radar.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/changes_in_period`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.radar.body) },
  ];
  observed.location = `${ORIGIN}/radar.html`;
  observed.text = "1 state of 1 work";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.radar, state: "success", texts: ["1 state of 1 work"] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "changes_in_period", parameters: { date_from: READING_DATE, date_to: READING_DATE, identifier: "x" } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was/.test(failure)), "a request carrying an identifier nobody typed fails");
});
