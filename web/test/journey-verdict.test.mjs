// The journey's verdict, held on observations built to pass and on each way one can fail.
//
// The browser run itself needs a local API and a browser and is not part of this suite; what it
// concludes from what it observed is, so a verdict that let a failing page pass cannot land unnoticed.

import assert from "node:assert/strict";
import test from "node:test";

import {
  ADVICE_QUESTION,
  ASK_PRIMARY_TEXT_ROUTES,
  DOSSIER_IDENTIFIER,
  EARLY_READING_DATE,
  EU_DOSSIER_ON_FIXTURE,
  EU_DOSSIER_STEP,
  EU_SEARCH_IDENTIFIER,
  EU_SEARCH_ON_FIXTURE,
  EU_SEARCH_PHRASE,
  EU_SEARCH_STEP,
  HISTORY_ANCHOR,
  JOURNEY_STEPS,
  MCP_PROTOCOL_VERSION,
  NO_HIT_SEARCH_STEP,
  READING_DATE,
  SEARCH_PHRASE,
  UNKNOWN_LAW,
  askCardFailures,
  envelopeIdentityFailures,
  expectedFromEnvelope,
  fixtureMountExpectations,
  journeyVerdict,
  pinnedCitation,
  pageRequestBodies,
  realMountSteps,
  specificationJourneyExpectations,
  twoStateExpectations,
  europeAnnexExpectations,
  withoutRequestFields,
  EU_READING_DATE,
  EU_READING_STEP,
  watchFiles,
} from "../scripts/journey.mjs";
import { cspValue } from "../scripts/csp.mjs";
import { EXPORT_WATERMARK } from "../scripts/export-build.mjs";

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
  assert.equal(JOURNEY_STEPS.export.then.count, 3, "journey J4: an answer across several provisions, three pinned for one export");
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
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, placed: 0 } }, expected).some((failure) => failure === "Tab reached 0 of the form's 1 fields"));
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, stops: [stop("button", "submit", false, "Search")] } }, expected)
    .some((failure) => failure === 'a focus stop shows no focus indicator: button[type=submit] "Search"'), "a suppressed focus ring fails");
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, keyPresses: 0 } }, expected)
    .some((failure) => failure === "text arrived without key presses: 18 characters typed, 0 character keys pressed"), "text set without key events is not typing (review of #802)");
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, keyPresses: undefined } }, expected)
    .some((failure) => failure.startsWith("text arrived without key presses")), "a count the page never kept fails too");
  assert.ok(journeyVerdict({ ...observed, keyboard: undefined }, expected).some((failure) => failure === "the run took no keyboard path"));
  assert.deepEqual(journeyVerdict({ ...observed, keyboard: undefined }, { ...expected, keyboard: false }), [], "a run not asked to use the keyboard is not judged on it");
});

test("every citation the page prints is hash-pinned and verifies as the state and article it pins", () => {
  const digest = "a".repeat(64);
  const state = `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${digest}`;
  const article = `${state}#art_15`;
  const matches = (identifier, anchor = null) => ({ identifier, refusal: null, verdict: "digest_matches", stateSha256: digest, requestedAnchor: anchor });
  const observed = { ...goodSearch(), citations: [state, article], verifications: [matches(state), matches(article, "art_15")] };
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const failing = (change) => journeyVerdict({ ...observed, ...change }, expected);
  assert.deepEqual(journeyVerdict(observed, expected), [], "pinned citations that verify pass");

  assert.ok(failing({ citations: [...observed.citations, "/lu-legilux/loi-1991-08-10-n3/2024-02-01"] })
    .includes("the page printed /lu-legilux/loi-1991-08-10-n3/2024-02-01, which is not a hash-pinned permalink"), "an unpinned permalink fails");
  assert.ok(failing({ citations: [], verifications: [] }).includes("the page printed no citation"), "an answer of a citing step that cites nothing fails");
  assert.deepEqual(journeyVerdict({ ...observed, citations: [], verifications: [], emptyAnswer: true }, { ...expected, nothingToCite: true })
    .filter((failure) => failure.includes("cite")), [], "an answer holding nothing to cite, which the page says, need not cite");
  const hidden = failing({ citations: [], verifications: [], emptyAnswer: true });
  assert.ok(hidden.includes("the page printed no citation"), "the page's own 'no hit' excuses nothing while the API's answer holds a hit (review of #815)");
  assert.ok(hidden.includes("the page says it holds nothing to cite, and the API's answer holds something"));
  assert.deepEqual(journeyVerdict({ ...goodSearch(), answerState: "refusal", citations: [], verifications: [] }, { ...expected, state: "refusal" })
    .filter((failure) => failure.includes("citation")), [], "a refusal need not cite");
  assert.ok(failing({ verifications: [matches(state)] }).includes(`${article} was not verified`), "a citation left unverified fails");
  assert.ok(failing({ verifications: [matches(state), { ...matches(article, "art_15"), refusal: "no_version_for_date", verdict: null }] })
    .includes(`verify refused ${article} with no_version_for_date`));
  assert.ok(failing({ verifications: [matches(state), { ...matches(article, "art_15"), verdict: "digest_mismatch" }] })
    .includes(`verify found ${article} digest_mismatch, not digest_matches`));
  assert.ok(failing({ verifications: [matches(state), { ...matches(article, "art_15"), stateSha256: "b".repeat(64) }] })
    .includes(`verify of ${article} named the state ${"b".repeat(64)}`), "verify must find the state the citation pins");
  assert.ok(failing({ verifications: [matches(state), matches(article, "art_16")] })
    .includes(`verify of ${article} named the article art_16`), "and the article it names");
  assert.deepEqual(journeyVerdict(goodSearch(), expected).filter((failure) => failure.includes("citation")), [], "a run that read no citations is not judged on them");
});

test("every quotation carries its digests, its official source and a citation pinning its article (review of #805)", () => {
  const digest = "a".repeat(64);
  const state = `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${digest}`;
  const source = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo";
  const quoted = (article, codes = [digest, "b".repeat(64), source, `${state}#${article}`]) => ({ article, codes });
  const observed = { ...goodSearch(), quotes: [quoted("art_15"), quoted("art_16")] };
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const failing = (quotes) => journeyVerdict({ ...observed, quotes }, expected);
  assert.deepEqual(journeyVerdict(observed, expected), [], "quotes that carry all four pass");
  assert.ok(failing([quoted("art_15"), quoted("art_16", [digest, "b".repeat(64), source])])
    .includes("the quote of art_16 carries no citation that pins it"), "a quote cited only by its state fails (the export composer's 49 quotes)");
  assert.ok(failing([quoted("art_15"), quoted("art_16", [digest, "b".repeat(64), source, `${state}#art_15`])])
    .includes("the quote of art_16 carries no citation that pins it"), "a citation of another article does not cite this one");
  assert.ok(failing([quoted("art_15", [digest, source, `${state}#art_15`])])
    .includes("the quote of art_15 does not show its text digest, body digest and official source"), "one digest is not two");
  assert.ok(failing([quoted("art_15", [digest, "b".repeat(64), `${state}#art_15`])])
    .includes("the quote of art_15 does not show its text digest, body digest and official source"), "no official source");
});

test("an EU quotation is pinned by its EU wording permalink, its provision escaped as the platform escapes it (review of #903)", () => {
  const digest = "c".repeat(64);
  const wording = `/eu-eurlex/32016R0679/eng/2016-04-27--${digest}`;
  const source = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/DOC_1";
  const quoted = (article, codes) => ({ article, codes });
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const verdict = (quotes) => journeyVerdict({ ...goodSearch(), quotes }, expected);
  assert.deepEqual(verdict([quoted("001", [digest, "d".repeat(64), source, `${wording}#001`])]), [], "an EU quote with its own provision permalink passes");
  assert.deepEqual(verdict([quoted("art 1", [digest, "d".repeat(64), source, `${wording}#art%201`])]), [], "a provision id is compared unescaped");
  assert.ok(verdict([quoted("002", [digest, "d".repeat(64), source, `${wording}#001`])])
    .includes("the quote of 002 carries no citation that pins it"), "another provision's permalink does not pin this one");
  assert.ok(verdict([quoted("001", [digest, "d".repeat(64), source, wording])])
    .includes("the quote of 001 carries no citation that pins it"), "the wording permalink alone pins no provision");
});

test("nothing on the page means anything by colour alone: painted elements say what they are", () => {
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  assert.deepEqual(journeyVerdict({ ...goodSearch(), paint: { painted: 4, unnamed: [] } }, expected), [], "a page whose painted elements all speak passes");
  assert.ok(journeyVerdict({ ...goodSearch(), paint: { painted: 4, unnamed: ["span.badge", "span.badge"] } }, expected)
    .includes("meaning by colour alone: span.badge painted with no words or accessible name"), "a painted, wordless badge fails, named once");
  assert.deepEqual(journeyVerdict(goodSearch(), expected), [], "a run that did not look is not judged on it");
});

test("on a real mount, a page is held to what the API answers its request: a success, or that refusal by its code", () => {
  assert.deepEqual(expectedFromEnvelope({ verdict: "answer", result: {} }), { state: "success", nothingToCite: false });
  assert.deepEqual(expectedFromEnvelope({ verdict: "answer", result: { value: { hits: [] } } }), { state: "success", nothingToCite: true }, "a search with no hit");
  assert.deepEqual(expectedFromEnvelope({ verdict: "answer", result: { value: { changes: [] } } }), { state: "success", nothingToCite: true }, "a radar window with no row");
  assert.deepEqual(expectedFromEnvelope({ verdict: "answer", result: { value: { hits: [{}] } } }), { state: "success", nothingToCite: false }, "a search with a hit");
  assert.deepEqual(expectedFromEnvelope({ verdict: "refuse", refusal: { code: "identifier_unknown" } }), { state: "refusal", refusalCode: "identifier_unknown" });
  assert.throws(() => expectedFromEnvelope({ verdict: "refuse" }), /neither an answer nor a typed refusal/, "a refusal without its code sets no expectation");
  assert.throws(() => expectedFromEnvelope({ type: "urn:lex:v3:transport:request_schema_invalid" }), /neither an answer nor a typed refusal/, "a transport failure is not an answer to hold a page to");
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

test("on the fixture mount every step must answer, and coverage must name the mount's own digests", () => {
  const mount = { corpus_sha256: "a".repeat(64), index_sha256: "b".repeat(64), work_key: "lu-legilux/loi-1991-08-10-n3" };
  const expectations = fixtureMountExpectations(mount);
  assert.deepEqual(expectations.map(([name]) => name).sort(), Object.keys(JOURNEY_STEPS).sort(), "each of the eight steps, once");
  assert.ok(expectations.every(([name, expected]) => expected.step === JOURNEY_STEPS[name] && expected.state === "success"), "each is its own step, answered");
  const [, coverage] = expectations[0];
  assert.equal(coverage.corpusSha256, mount.corpus_sha256);
  assert.equal(coverage.indexSha256, mount.index_sha256);
  assert.ok(expectations.slice(1).every(([, expected]) => expected.texts.length > 0), "every other step names the texts it must show");
  assert.ok(expectations.find(([name]) => name === "dossier")[1].texts.includes(mount.work_key), "the dossier names the mount's work");
});

test("on the licence-blocked mount no page may show or carry an article's words, and reading and export must refuse text_withheld", () => {
  const withheld = "L’Assemblée générale annuelle se tient";
  const expected = { ...SEARCH, absentTexts: [withheld] };
  assert.deepEqual(journeyVerdict(goodSearch(), expected), [], "a page without the words passes");
  const shown = { ...goodSearch(), text: `${goodSearch().text}
L’Assemblée  générale
annuelle se tient le premier lundi` };
  assert.ok(journeyVerdict(shown, expected).includes(`the page shows withheld text "${withheld}"`), "shown, whatever the whitespace");
  const carried = { ...goodSearch(), html: `<div hidden>${withheld}</div>` };
  assert.ok(journeyVerdict(carried, expected).includes(`the page's markup carries withheld text "${withheld}"`), "carried in hidden markup");

  const refusal = { ...goodSearch(), answerState: "refusal", text: `${goodSearch().text} text_withheld`, refusalCodes: ["text_withheld"] };
  assert.ok(!journeyVerdict(refusal, { ...SEARCH, state: "refusal", refusalCode: "text_withheld", mustRefuse: "text_withheld" }).some((failure) => failure.startsWith("the step must refuse")), "the refusal the licence demands");
  assert.ok(journeyVerdict(goodSearch(), { ...SEARCH, mustRefuse: "text_withheld" }).includes("the step must refuse text_withheld, and it was held to an answer"), "an answer where the licence must withhold the text");
  assert.ok(journeyVerdict(refusal, { ...SEARCH, state: "refusal", refusalCode: "identifier_unknown", mustRefuse: "text_withheld" })
    .includes("the step must refuse text_withheld, and it was held to the refusal identifier_unknown"), "another refusal is not the licence's");

  // The review of #834's two reproductions: another card with the code mentioned elsewhere, and a passage past an article's opening.
  const otherCard = { ...refusal, text: "identifier_unknown; diagnostic mentions text_withheld", refusalCodes: ["identifier_unknown"] };
  const held = { ...SEARCH, state: "refusal", refusalCode: "text_withheld", mustRefuse: "text_withheld" };
  const failures = journeyVerdict(otherCard, held);
  assert.ok(failures.includes("the page's refusal card shows [\"identifier_unknown\"], not text_withheld"), failures.join("\n"));
  assert.ok(failures.includes("the step must show the refusal card text_withheld, and it shows [\"identifier_unknown\"]"));
  assert.ok(journeyVerdict({ ...refusal, refusalCodes: [] }, held).includes("the step must show the refusal card text_withheld, and it shows []"), "no card at all");
  const later = "confidential later article words are expo";
  const leaked = { ...refusal, text: `text_withheld; ${later}sed here` };
  assert.ok(journeyVerdict(leaked, { ...held, absentTexts: ["Opening forty characters of the article", later] }).includes(`the page shows withheld text "${later}"`),
    "a window past the opening is looked for too");
});

test("the EU search step names the GDPR by its CELEX, chooses English and is held to the exact body", () => {
  assert.deepEqual(EU_SEARCH_STEP.typed, [EU_SEARCH_PHRASE, EU_SEARCH_IDENTIFIER]);
  assert.deepEqual(EU_SEARCH_STEP.chosen, { value: "eng", label: "English" });
  assert.deepEqual(EU_SEARCH_STEP.body, { operation_id: "search", parameters: { query: "personal data", language: "eng", identifier: "32016R0679" } },
    "in the order the page sends them: the phrase, the language, the work");
  assert.equal(EU_SEARCH_STEP.path, JOURNEY_STEPS.search.path, "the same search page");
  assert.ok(!Object.values(JOURNEY_STEPS).includes(EU_SEARCH_STEP), "not one of the eight steps every mount runs: it needs an EU index");

  const report = { corpus: { Sha256: "a".repeat(64) }, luxembourgIndex: { Sha256: "b".repeat(64) } };
  assert.deepEqual(realMountSteps(report).map(([name]) => name), Object.keys(JOURNEY_STEPS), "a real mount without an EU index runs the eight");
  const withEurope = realMountSteps({ ...report, europeIndex: { Sha256: "c".repeat(64) } });
  assert.deepEqual(withEurope.map(([name]) => name), [...Object.keys(JOURNEY_STEPS), "eu search", "eu dossier", "eu reading"],
    "and one with an EU index runs the EU search, dossier and reading too");
  assert.equal(withEurope.at(-3)[1], EU_SEARCH_STEP);
  assert.equal(withEurope.at(-2)[1], EU_DOSSIER_STEP);
  assert.equal(withEurope.at(-1)[1], EU_READING_STEP);
  // The EU reading: the same work on its wording date (the GDPR's Formex act date), on the reading page.
  assert.deepEqual(EU_READING_STEP.body, { operation_id: "evidence_bundle", parameters: { identifier: "32016R0679", date: EU_READING_DATE } });
  assert.equal(EU_READING_DATE, "2016-04-27");
  assert.equal(EU_READING_STEP.path, JOURNEY_STEPS.reading.path, "the same reading page");
  assert.ok(EU_READING_STEP.cites, "every EU article permalink the page prints is verified");
  assert.deepEqual(EU_DOSSIER_STEP.body, { operation_id: "dossier", parameters: { identifier: "32016R0679" } }, "the same work, any held language");
  assert.equal(EU_DOSSIER_STEP.path, JOURNEY_STEPS.dossier.path, "the same dossier page");
  assert.deepEqual(EU_DOSSIER_ON_FIXTURE, { ...EU_SEARCH_ON_FIXTURE, step: EU_DOSSIER_STEP });

  assert.deepEqual(EU_SEARCH_ON_FIXTURE, { step: EU_SEARCH_STEP, state: "refusal", refusalCode: "no_corpus_mounted", texts: ["This build has no EU index mounted."] },
    "the fixture mount holds no EU index, and the page must say that one is what is missing");
});

test("an EU citation is pinned by its wording, verifies as that wording, and names its provision unescaped", () => {
  const digest = "c".repeat(64);
  const wording = `/eu-eurlex/32016R0679/eng/2016-04-27--${digest}`;
  const article = `${wording}#026`;
  const escaped = `${wording}#26%28a%29`;
  assert.deepEqual(pinnedCitation(article), { publisher: "eu-eurlex", digest, anchor: "026" });
  assert.deepEqual(pinnedCitation(escaped), { publisher: "eu-eurlex", digest, anchor: "26(a)" }, "verify names the provision unescaped");
  assert.deepEqual(pinnedCitation(wording), { publisher: "eu-eurlex", digest, anchor: null });
  assert.equal(pinnedCitation(`${wording}#%E0%A4%A`), null, "a provision that is not a valid escape pins nothing");
  assert.equal(pinnedCitation("/eu-eurlex/32016R0679/eng/2016-04-27"), null, "a permalink without its digest pins nothing");
  assert.equal(pinnedCitation(`http://publications.europa.eu/resource/cellar/x.0006#lex-provision=026`), null, "nor does the provision coordinate");

  const matches = (identifier, anchor = null) => ({ identifier, refusal: null, verdict: "digest_matches", publisher: "eu-eurlex", stateSha256: null, wordingSha256: digest, requestedAnchor: anchor });
  const step = EU_SEARCH_STEP;
  const observed = {
    ...goodSearch(),
    requests: goodSearch().requests.map((request) => (request.postData ? { ...request, postData: JSON.stringify(step.body) } : request)),
    citations: [wording, article, escaped],
    verifications: [matches(wording), matches(article, "026"), matches(escaped, "26(a)")],
  };
  const expected = { origin: ORIGIN, step, state: "success" };
  const failing = (change) => journeyVerdict({ ...observed, ...change }, expected);
  assert.deepEqual(journeyVerdict(observed, expected), [], "EU citations that verify as their wording pass");
  assert.ok(failing({ verifications: [matches(wording), { ...matches(article, "026"), wordingSha256: "d".repeat(64) }, matches(escaped, "26(a)")] })
    .includes(`verify of ${article} named the wording ${"d".repeat(64)}`), "verify must find the wording the citation pins");
  assert.ok(failing({ verifications: [matches(wording), { ...matches(article, "026"), wordingSha256: null, stateSha256: digest }, matches(escaped, "26(a)")] })
    .includes(`verify of ${article} named the wording null`), "a state digest is not a wording digest");
  assert.ok(failing({ verifications: [matches(wording), matches(article, "026"), matches(escaped, "26%28a%29")] })
    .includes(`verify of ${escaped} named the article 26%28a%29`), "the provision is compared unescaped");
  assert.ok(failing({ verifications: [matches(wording), { ...matches(article, "026"), publisher: "lu-legilux" }, matches(escaped, "26(a)")] })
    .includes(`verify of ${article} answered for lu-legilux`), "an EU citation is verified by the EU index");
  assert.ok(failing({ verifications: [matches(wording), { ...matches(article, "026"), refusal: "pinned_digest_mismatch", verdict: null }, matches(escaped, "26(a)")] })
    .includes(`verify refused ${article} with pinned_digest_mismatch`));
  assert.ok(failing({ citations: [...observed.citations, "/eu-eurlex/32016R0679/eng/2016-04-27"] })
    .includes("the page printed /eu-eurlex/32016R0679/eng/2016-04-27, which is not a hash-pinned permalink"));
});

test("a keyboard run that chooses an option counts the select among the fields Tab must reach", () => {
  const step = EU_SEARCH_STEP;
  const observed = {
    ...goodSearch(),
    requests: goodSearch().requests.map((request) => (request.postData ? { ...request, postData: JSON.stringify(step.body) } : request)),
    keyboard: { stops: [], placed: 3, wanted: 3, characters: 24, keyPresses: 24 },
  };
  const expected = { origin: ORIGIN, step, state: "success", keyboard: true };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, keyboard: { ...observed.keyboard, placed: 2 } }, expected).includes("Tab reached 2 of the form's 3 fields"),
    "a select never reached fails as a field never reached");
});

test("the specification's journeys the eight steps do not walk are held to their own outcome: J1's refusal, J2's no hit, J5's law not held", () => {
  const expectations = specificationJourneyExpectations();
  assert.deepEqual(expectations.map(([name]) => name.slice(0, 2)), ["J1", "J2", "J5"]);
  for (const [name, expected] of expectations) {
    assert.equal(expected.step.body.operation_id, expected.step.operation, name);
    assert.ok(expected.texts.length > 0, `${name} names the texts it must show`);
  }
  const byJourney = Object.fromEntries(expectations.map(([name, expected]) => [name.slice(0, 2), expected]));
  assert.deepEqual(byJourney.J1.step.body.parameters, { identifier: DOSSIER_IDENTIFIER, date: EARLY_READING_DATE });
  assert.ok(EARLY_READING_DATE < READING_DATE, "J1 asks a date before the fixture's one state");
  assert.equal(byJourney.J1.refusalCode, "no_version_for_date");
  assert.equal(byJourney.J2.state, "success");
  assert.equal(byJourney.J2.nothingToCite, true, "a search with no hit cites nothing, and says so");
  assert.equal(byJourney.J5.refusalCode, "identifier_unknown");
  assert.deepEqual(byJourney.J5.step.body.parameters, { identifier: UNKNOWN_LAW });

  // J2's page with no hit is excused from citing only because it says it holds nothing, and it must say that
  // the absence is not evidence that the law does not exist.
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/search.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/search`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(NO_HIT_SEARCH_STEP.body) },
  ];
  observed.text = byJourney.J2.texts.join(" ");
  observed.citations = [];
  observed.emptyAnswer = true;
  const expected = { origin: ORIGIN, ...byJourney.J2 };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, emptyAnswer: false }, expected).includes("the page printed no citation"), "a page that does not say it holds nothing must cite");
  assert.ok(journeyVerdict({ ...observed, text: byJourney.J2.texts[0] }, expected).some((failure) => /It is not evidence/.test(failure)), "the absence note is required");
});

test("journey J7: REST and MCP answer one envelope, apart from the request's own reference and the moment it was answered", () => {
  const envelope = (refFill, at, verdict = "answer") => ({
    verdict,
    request_ref: refFill.repeat(64),
    context: { publisher: "lu-legilux", freshness: { observed_at: at, built_at: "2026-10-01T00:00:00Z" } },
    result: { value: { hits: [] } },
  });
  const rest = { status: 200, json: envelope("a", "2026-10-03T02:00:00Z") };
  const tool = (structured, text = JSON.stringify(structured)) => ({
    status: 200,
    protocolVersion: MCP_PROTOCOL_VERSION,
    json: { jsonrpc: "2.0", id: "search", result: { isError: false, structuredContent: structured, content: [{ type: "text", text }] } },
  });
  assert.deepEqual(envelopeIdentityFailures(rest, tool(envelope("b", "2026-10-03T02:00:01Z"))), [], "the request reference and the observation time may differ");
  assert.deepEqual(withoutRequestFields(rest.json).context.freshness, { built_at: "2026-10-01T00:00:00Z" }, "only the observation time is set aside");

  const refused = envelope("b", "2026-10-03T02:00:01Z", "refuse");
  assert.ok(envelopeIdentityFailures(rest, tool(refused)).some((failure) => /different envelopes, first at \.verdict/.test(failure)), "a different verdict is named");
  const built = envelope("b", "2026-10-03T02:00:01Z");
  built.context.freshness.built_at = "2026-10-02T00:00:00Z";
  assert.ok(envelopeIdentityFailures(rest, tool(built)).some((failure) => /context\.freshness\.built_at/.test(failure)), "any other field of freshness must agree");
  assert.ok(envelopeIdentityFailures(rest, tool(envelope("a", "x"), "{}")).some((failure) => /text and its structured content differ/.test(failure)), "the text is the structured content");
  assert.ok(envelopeIdentityFailures(rest, { ...tool(envelope("a", "x")), protocolVersion: null }).some((failure) => /protocol none/.test(failure)), "the protocol revision is stated");
  assert.ok(envelopeIdentityFailures(rest, { status: 200, protocolVersion: MCP_PROTOCOL_VERSION, json: { jsonrpc: "2.0", id: 1, error: { code: -32602 } } }).some((failure) => /no tool result/.test(failure)), "a JSON-RPC error fails");
  const flagged = tool(envelope("a", "x"));
  flagged.json.result.isError = true;
  assert.ok(envelopeIdentityFailures(rest, flagged).some((failure) => /isError true/.test(failure)), "a tool error fails");
  assert.ok(envelopeIdentityFailures({ ...rest, status: 500 }, tool(envelope("a", "x"))).some((failure) => /REST answered HTTP 500/.test(failure)));
});

test("journey J7 asks every request the journeys' pages make, each once", () => {
  const bodies = pageRequestBodies();
  const keys = bodies.map((body) => JSON.stringify(body));
  assert.equal(new Set(keys).size, keys.length, "each request once");
  for (const step of [...Object.values(JOURNEY_STEPS), ...specificationJourneyExpectations().map(([, expected]) => expected.step)]) {
    assert.ok(keys.includes(JSON.stringify(step.body ?? { operation_id: "coverage", parameters: {} })), `${step.path} ${step.operation}`);
  }
});

test("journey J6: ask answers the contained assistant's card, never a conclusion, and hands the reader to the primary text", () => {
  const card = () => ({
    verdict: "point",
    result: {
      value: {
        presentation_result: "assistant_v3_unavailable",
        containment: { decisions: ["51", "91"], model_gloss: "disabled" },
        question_read: false,
        deterministic_actions: ["resolve", "search", "as_of", "evidence_bundle"].map((operation) => ({ operation_id: operation, route: `/api/v3/${operation}` })),
      },
    },
  });
  assert.deepEqual(askCardFailures(card()), []);
  assert.deepEqual(ASK_PRIMARY_TEXT_ROUTES, ["resolve", "as_of", "evidence_bundle"]);
  const answered = card();
  answered.verdict = "answer";
  assert.ok(askCardFailures(answered).some((failure) => /verdict answer, not point/.test(failure)), "an answer is a conclusion");
  const read = card();
  read.result.value.question_read = true;
  assert.ok(askCardFailures(read).some((failure) => /not read/.test(failure)));
  const glossed = card();
  glossed.result.value.containment.model_gloss = "enabled";
  assert.ok(askCardFailures(glossed).some((failure) => /model gloss is enabled/.test(failure)));
  const narrow = card();
  narrow.result.value.deterministic_actions = narrow.result.value.deterministic_actions.filter((action) => action.operation_id !== "evidence_bundle");
  assert.ok(askCardFailures(narrow).some((failure) => /evidence_bundle/.test(failure)), "the card must hand the reader to the quoted text");
  const misrouted = card();
  misrouted.result.value.deterministic_actions[0].route = "/api/v3/ask";
  assert.ok(askCardFailures(misrouted).some((failure) => /names the route \/api\/v3\/ask/.test(failure)));
  const echoed = card();
  echoed.result.value.note = `you asked: ${ADVICE_QUESTION}`;
  assert.ok(askCardFailures(echoed).some((failure) => /question's words/.test(failure)), "nothing of the question is in the card");
});


test("on the two-state mount journey J3 compares two different states and journey J8's radar shows the change", () => {
  const mount = { work_key: "lu-legilux/loi-1991-08-10-n3", first_date: READING_DATE, later_date: "2025-01-01", amended_article: "art_7" };
  const [[j3, compare], [j8, radar]] = twoStateExpectations(mount);
  assert.match(j3, /^J3/);
  assert.match(j8, /^J8/);
  assert.equal(compare.step.operation, "diff");
  assert.deepEqual(compare.step.typed, [DOSSIER_IDENTIFIER, READING_DATE, "2025-01-01"]);
  assert.deepEqual(compare.step.body.parameters, { identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: "2025-01-01" });
  assert.ok(compare.texts.includes("art_7: changed"), "the amended article is named as changed");
  assert.ok(compare.texts.includes("1 changed, 0 added, 0 removed"));
  assert.equal(radar.step.operation, "changes_in_period");
  assert.deepEqual(radar.step.body.parameters, { date_from: READING_DATE, date_to: "2025-01-01" });
  assert.ok(radar.texts.includes(`wording changed from the state of ${READING_DATE}`), "the later state is compared with the one it replaced");
  assert.equal(JOURNEY_STEPS.compare.typed[1], JOURNEY_STEPS.compare.typed[2], "the one-state fixture's compare asks one date twice, the reason this mount exists");
});

test("on the EU annex control mount the reading and dossier pages say the annex is not served as text, and never show it", () => {
  const mount = { eu_annex: { celex: "32016R0679", wording_date: "2026-08-26", annexes: 1, absent_texts: ["Hambali", "ANNEXSENTINELZQXV"] } };
  const [[readName, read], [dossierName, dossier]] = europeAnnexExpectations(mount);
  assert.match(readName, /read on its wording date/);
  assert.match(dossierName, /dossier/);
  assert.equal(read.step.operation, "evidence_bundle");
  assert.deepEqual(read.step.typed, ["32016R0679", "2026-08-26"]);
  assert.deepEqual(read.step.body.parameters, { identifier: "32016R0679", date: "2026-08-26" });
  assert.equal(dossier.step.operation, "dossier");
  assert.deepEqual(dossier.step.body.parameters, { identifier: "32016R0679" });
  for (const expected of [read, dossier]) {
    assert.equal(expected.state, "success");
    assert.deepEqual(expected.texts, ["1 annex of the English wording is not served as text, and is never searched, quoted or exported"]);
    assert.deepEqual(expected.absentTexts, ["Hambali", "ANNEXSENTINELZQXV"], "the annex's text and title appear nowhere on either page");
  }
  assert.throws(() => europeAnnexExpectations({ eu_annex: { ...mount.eu_annex, absent_texts: [] } }), /no annex text to look for/);
});

test("on the EU annex control mount the export composer pins both articles and shows the EU export, its annex excluded and never its text", () => {
  const mount = { eu_annex: { celex: "32016R0679", wording_date: "2026-08-26", annexes: 1, absent_texts: ["Hambali", "ANNEXSENTINELZQXV"] } };
  const [, , [name, exporting]] = europeAnnexExpectations(mount);
  assert.match(name, /exported/);
  assert.equal(exporting.step.path, "/export.html");
  assert.deepEqual(exporting.step.typed, ["32016R0679", "2026-08-26"]);
  assert.deepEqual(exporting.step.body, { operation_id: "evidence_bundle", parameters: { identifier: "32016R0679", date: "2026-08-26" } }, "the export asks the reading, and nothing else");
  assert.deepEqual(exporting.step.then, { click: "input[data-pin]", count: 2, until: "[data-export-state=composed] [data-export-annexes]" }, "both articles pinned, and the export must list the annex");
  assert.deepEqual(exporting.texts, [
    "1 annex of the English wording is not served as text, and is never searched, quoted or exported",
    "2 articles pinned: 2 exported with text, 0 excluded.",
    EXPORT_WATERMARK,
    "© European Union, https://eur-lex.europa.eu",
    "Save as PDF",
  ]);
  assert.deepEqual(exporting.absentTexts, ["Hambali", "ANNEXSENTINELZQXV"], "the annex's text and title appear nowhere on the page");

  // Held to it: the composed export passes; the annex's text in the JSON the page shows, no annex listed, or no PDF fails.
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/export.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(exporting.step.body) },
  ];
  observed.location = `${ORIGIN}/export.html`;
  observed.text = exporting.texts.join("\n");
  observed.html = '<details><summary>The JSON as it will be saved</summary><pre>{"annexes_not_served": [{"annexes": 1}]}</pre></details>';
  observed.then = "reached";
  const expected = { origin: ORIGIN, ...exporting };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, html: '<pre>{"text": "Hambali"}</pre>' }, expected).includes('the page\'s markup carries withheld text "Hambali"'), "the annex's text in the JSON shown");
  assert.ok(journeyVerdict({ ...observed, then: "clicked" }, expected).some((failure) => /never showed \[data-export-state=composed\] \[data-export-annexes\]/.test(failure)), "no annex listed");
  assert.ok(journeyVerdict({ ...observed, text: observed.text.replace("Save as PDF", "") }, expected).includes('the page does not show "Save as PDF"'), "no PDF offered");
});
