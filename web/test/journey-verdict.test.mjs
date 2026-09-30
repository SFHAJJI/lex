// The journey's verdict, held on observations built to pass and on each way one can fail.
//
// The browser run itself needs a local API and a browser and is not part of this suite; what it
// concludes from what it observed is, so a verdict that let a failing page pass cannot land unnoticed.

import assert from "node:assert/strict";
import test from "node:test";

import { DOSSIER_IDENTIFIER, HISTORY_ANCHOR, JOURNEY_STEPS, READING_DATE, SEARCH_PHRASE, journeyVerdict, watchFiles } from "../scripts/journey.mjs";
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

test("an export run reads, pins, and is held to the composed export and to its one request", () => {
  assert.deepEqual(JOURNEY_STEPS.export.typed, [DOSSIER_IDENTIFIER, READING_DATE]);
  assert.deepEqual(JOURNEY_STEPS.export.body, JOURNEY_STEPS.reading.body, "the export asks the reading, and nothing else");
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/export.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.export.body) },
  ];
  observed.location = `${ORIGIN}/export.html`;
  observed.text = "1 article pinned: 1 exported with text, 0 excluded.";
  observed.then = "reached";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.export, state: "success", texts: ["1 article pinned"] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  for (const then of ["clicked", "absent", undefined]) {
    assert.ok(journeyVerdict({ ...observed, then }, expected).some((failure) => /never showed \[data-export-state=composed\]/.test(failure)), `a run whose pin ${then ?? "was never clicked"} fails`);
  }
  const twice = { ...observed, requests: [...observed.requests, observed.requests[1]] };
  assert.ok(journeyVerdict(twice, expected).some((failure) => /2 requests to the API/.test(failure)), "composing must not ask again");
  const refused = { ...observed, answerState: "refusal", text: "no_corpus_mounted", then: undefined };
  assert.deepEqual(journeyVerdict(refused, { origin: ORIGIN, step: JOURNEY_STEPS.export, state: "refusal", refusalCode: "no_corpus_mounted" }), [], "a refusal is not pinned");
});

test("a run is held to the API process recording nothing: no query text, user agent, address, output or file", () => {
  const observed = goodSearch();
  observed.userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) HeadlessChrome/140.0.0.0";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  assert.deepEqual(journeyVerdict({ ...observed, api: { output: "", changedFiles: [] } }, expected), [], "silent and file-less passes");
  const startup = "lex_v3_preview_bootstrap_failed reason=immutable_custody\n";
  assert.deepEqual(journeyVerdict({ ...observed, api: { startup, output: "", changedFiles: [] } }, expected), [], "a startup message written before the first answer is the process's startup, not the run's");
  assert.ok(journeyVerdict({ ...observed, api: { startup: `${startup}${SEARCH_PHRASE}`, output: "", changedFiles: [] } }, expected).some((failure) => failure.includes("recorded the query text")), "startup output is still held to carrying none of the run's text");
  const failing = (api) => journeyVerdict({ ...observed, api }, expected);
  assert.ok(failing({ output: `POST /api/v3/search ${SEARCH_PHRASE}\n`, changedFiles: [] }).some((failure) => failure.includes(`recorded the query text "${SEARCH_PHRASE}"`)));
  assert.ok(failing({ output: `ua=${observed.userAgent}`, changedFiles: [] }).some((failure) => failure.includes("recorded the browser's user agent")));
  assert.ok(failing({ output: "client 127.0.0.1:53211", changedFiles: [] }).some((failure) => failure.includes("recorded an address")));
  assert.ok(failing({ output: "info: request served\n", changedFiles: [] }).some((failure) => failure.startsWith("the API process wrote output during the run")), "any output at all fails");
  assert.ok(failing({ output: "", changedFiles: ["logs/requests.log"] }).some((failure) => failure === "the API process wrote files: logs/requests.log"));
  assert.ok(failing({ output: "", changedFiles: [], fileEvents: [["rename", "request.log"]] }).some((failure) => failure === "the API process touched files while serving the run: request.log"));
  assert.deepEqual(journeyVerdict(observed, expected), [], "a run that observed no process is judged on the page alone");
});

test("a file written and deleted while the run is watched fails it, though both listings match (review of #801)", async () => {
  const { mkdtemp, rm, unlink, writeFile } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const home = await mkdtemp(join(tmpdir(), "lex-journey-watch-"));
  try {
    const observed = goodSearch();
    const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
    const quiet = watchFiles(home);
    assert.deepEqual(journeyVerdict({ ...observed, api: { output: "", changedFiles: [], fileEvents: await quiet.stop() } }, expected), [], "an untouched directory passes");
    const watched = watchFiles(home);
    await writeFile(join(home, "request.log"), `POST /api/v3/search ${SEARCH_PHRASE}\n`);
    await unlink(join(home, "request.log"));
    const fileEvents = await watched.stop();
    const failures = journeyVerdict({ ...observed, api: { output: "", changedFiles: [], fileEvents } }, expected);
    assert.ok(failures.some((failure) => failure.startsWith("the API process touched files while serving the run") && failure.includes("request.log")), failures.join("; "));
  } finally {
    await rm(home, { recursive: true, force: true });
  }
});

test("the answer is held to a polite live region that exists before it arrives (the screen-reader path)", () => {
  const observed = { ...goodSearch(), liveRegion: { atLoad: "polite", atEnd: "polite" } };
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, liveRegion: { atLoad: null, atEnd: "polite" } }, expected).some((failure) => failure.includes("at load is in no live region")), "a region created with the answer is not announced");
  assert.ok(journeyVerdict({ ...observed, liveRegion: { atLoad: "polite", atEnd: null } }, expected).some((failure) => failure.includes("arrived outside any live region")));
  assert.ok(journeyVerdict({ ...observed, liveRegion: { atLoad: "assertive", atEnd: "assertive" } }, expected).some((failure) => failure.includes("aria-live=assertive, not polite")), "nothing here is urgent enough to interrupt");
});

test("a keyboard run reaches every field by Tab alone, and every stop shows where focus is", () => {
  const stop = (tag, type, ring, label = "") => ({ tag, type, inForm: true, pin: false, label, ring });
  const observed = {
    ...goodSearch(),
    keyboard: { stops: [stop("a", null, true, "Skip"), stop("input", "text", true, "Phrase")], placed: 1, wanted: 1, characters: 18, keyPresses: 18 },
  };
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success", keyboard: true };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, placed: 0 } }, expected).some((failure) => failure === "Tab reached 0 of the form's 1 text fields"));
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, stops: [stop("button", "submit", false, "Search")] } }, expected)
    .some((failure) => failure === 'a focus stop shows no focus indicator: button[type=submit] "Search"'), "a suppressed focus ring fails");
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, keyPresses: 0 } }, expected)
    .some((failure) => failure === "text arrived without key presses: 18 characters typed, 0 character keys pressed"), "text set without key events is not typing (review of #802)");
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, keyPresses: undefined } }, expected)
    .some((failure) => failure.startsWith("text arrived without key presses")), "a count the page never kept fails too");
  assert.ok(journeyVerdict({ ...observed, keyboard: undefined }, expected).some((failure) => failure === "the run took no keyboard path"));
  assert.deepEqual(journeyVerdict({ ...observed, keyboard: undefined }, { ...expected, keyboard: false }), [], "a run not asked to use the keyboard is not judged on it");
});

test("a run whose page the API served is held to the headers the page arrived with (Decision 95, ruling 3)", () => {
  const policy = `${cspValue()}; frame-ancestors 'none'`;
  const served = () => ({
    ...goodSearch(),
    pageHeaders: {
      "Content-Security-Policy": policy,
      "Strict-Transport-Security": "max-age=31536000; includeSubDomains",
      "Referrer-Policy": "no-referrer",
      "X-Content-Type-Options": "nosniff",
    },
  });
  const expected = { ...SEARCH, servedByApi: true };
  assert.deepEqual(journeyVerdict(served(), expected), []);
  const cases = [
    ["no frame-ancestors", (o) => { o.pageHeaders["Content-Security-Policy"] = cspValue(); }, /arrived with the CSP/],
    ["no HSTS", (o) => { delete o.pageHeaders["Strict-Transport-Security"]; }, /without HSTS/],
    ["a referrer policy that sends one", (o) => { o.pageHeaders["Referrer-Policy"] = "origin"; }, /Referrer-Policy origin/],
    ["no nosniff", (o) => { delete o.pageHeaders["X-Content-Type-Options"]; }, /without nosniff/],
    ["headers never observed", (o) => { o.pageHeaders = null; }, /arrived with the CSP null/],
  ];
  for (const [what, mutate, reason] of cases) {
    const observed = served();
    mutate(observed);
    assert.ok(journeyVerdict(observed, expected).some((failure) => reason.test(failure)), what);
  }
  assert.deepEqual(journeyVerdict({ ...goodSearch(), pageHeaders: null }, SEARCH), [], "a page served by serve-live is not held to them");
});
