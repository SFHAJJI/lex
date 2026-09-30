// The journey's verdict, held on observations built to pass and on each way one can fail.
//
// The browser run itself needs a local API and a browser and is not part of this suite; what it
// concludes from what it observed is, so a verdict that let a failing page pass cannot land unnoticed.

import assert from "node:assert/strict";
import test from "node:test";

import { journeyVerdict } from "../scripts/journey.mjs";
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
