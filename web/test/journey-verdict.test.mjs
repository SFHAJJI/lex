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
  KEPT_SEARCH_STEP,
  NOT_FOUND_STEP,
  addressExpectations,
  HISTORY_ANCHOR,
  READING_LANGUAGE,
  JOURNEY_STEPS,
  REFLOW_WIDTH,
  answerCitations,
  verifyEvidence,
  bodyOn,
  endAddressesOf,
  frontDoorOf,
  journeyStepsFor,
  NOT_ADVICE_SAID,
  TODAY_IN_LUXEMBOURG,
  todayInLuxembourgAt,
  pinnedStep,
  heldLuxembourgWorkFromAnswers,
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
  realMountExpectations,
  realMountRuns,
  expectedForAnswer,
  PRE_HISTORY_READING_STEP,
  PRE_HISTORY_DATE,
  NO_EARLIER_STATE,
  REAL_MOUNT_EVENTS_VERIFIED,
  ABSENCE_NOTE,
  UNKNOWN_LAW_STEP,
  eventsFailures,
  apiJourneyRuns,
  specificationJourneyExpectations,
  twoStateExpectations,
  europeAnnexExpectations,
  longActExpectations,
  withoutRequestFields,
  EU_READING_DATE,
  EU_READING_STEP,
  watchFiles,
  watchApiFiles,
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
      { url: `${ORIGIN}/en/coverage.html`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live.js`, method: "GET", headers: {} },
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

test("once loaded, the language menu leads to the same page in each language, its article included", () => {
  const at = (path, languageLinks) => ({ ...good(), location: `${ORIGIN}${path}`, languageLinks });
  assert.deepEqual(journeyVerdict(at("/en/coverage.html", { fr: "/fr/coverage.html", en: "/en/coverage.html" }), SUCCESS), []);

  const unrewritten = journeyVerdict(at("/en/coverage.html", { fr: "/fr/", en: "/en/" }), SUCCESS);
  assert.ok(unrewritten.some((failure) => failure === "the language menu's fr link leads to /fr/, not /fr/coverage.html"), unrewritten.join(" | "));
  const article = journeyVerdict(at("/en/coverage.html#art_15", { fr: "/fr/coverage.html", en: "/en/coverage.html#art_15" }), SUCCESS);
  assert.ok(article.some((failure) => failure === "the language menu's fr link leads to /fr/coverage.html, not /fr/coverage.html#art_15"), "the article named after # goes along");
  const missing = journeyVerdict(at("/en/coverage.html", {}), SUCCESS);
  assert.ok(missing.some((failure) => failure === "the language menu's fr link leads to nothing, not /fr/coverage.html"), "a menu without the links fails");
});

test("a page that never arrived is one failure, named, and nothing the run could not read throws", () => {
  for (const location of ["chrome-error://chromewebdata/", "about:blank"]) {
    const observed = { ...good(), location, storage: undefined, text: undefined, requests: [] };
    assert.deepEqual(journeyVerdict(observed, SUCCESS), [`the page did not load: the browser stands at ${location}`]);
  }
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
    ["storage unread", (o) => { o.storage = { local: null, session: null }; }, /storage could not be read/],
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

/** Where a step's page ends on `today` (`endAddressesOf`): its path, with what the page writes into its own address. */
const endOf = (step, today = todayInLuxembourgAt(Date.now())) => `${ORIGIN}${endAddressesOf(step, [today])[0]}`;

function goodSearch() {
  return {
    ...good(),
    text: `“${SEARCH_PHRASE}” in fra: 4 with the exact phrase, 1 with every word, in 1 work. art_15 in x`,
    requests: [
      { url: `${ORIGIN}/en/search.html`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-search.js`, method: "GET", headers: {} },
      {
        url: `${ORIGIN}/api/v3/search`,
        method: "POST",
        headers: { "Content-Type": "application/json" },
        headersSent: true,
        postData: JSON.stringify(JOURNEY_STEPS.search.body),
      },
    ],
    location: endOf(JOURNEY_STEPS.search),
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
    ["the phrase in the address", (o) => { o.location = `${ORIGIN}/en/search.html?query=x`; }, /ended at .*\?query=x/],
    // The search after "#" (R109; the plan's item 3.5): kept there, and the one the page asked.
    ["the search not kept in the address", (o) => { o.location = `${ORIGIN}/en/search.html`; }, /ended at .*search\.html, not .*#q=/],
    ["another search kept", (o) => { o.location = `${ORIGIN}/en/search.html#q=x&lang=fra`; }, /ended at .*#q=x&lang=fra, not/],
    ["the phrase in the query beside it", (o) => { o.location = `${o.location.replace("#", "?q=x#")}`; }, /ended at .*\?q=x#/],
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

test("a page that scrolls sideways at 320 CSS pixels fails, naming what stands out past the window", () => {
  assert.equal(REFLOW_WIDTH, 320, "WCAG 2.2, 1.4.10: 320 CSS pixels");
  const fits = { ...goodSearch(), reflow: { width: 305, scrollWidth: 305, offenders: [] } };
  assert.deepEqual(journeyVerdict(fits, SEARCH), [], "a page as wide as its window passes");
  const wide = { ...fits, reflow: { width: 305, scrollWidth: 423, offenders: ["table in the answer 411 px", "tr in the answer 407 px"] } };
  assert.ok(journeyVerdict(wide, SEARCH).includes("at 320 CSS pixels the page scrolls sideways (423 px of content in a 305 px window): table in the answer 411 px, tr in the answer 407 px"));
  const unnamed = { ...fits, reflow: { width: 305, scrollWidth: 306, offenders: [] } };
  assert.ok(journeyVerdict(unnamed, SEARCH).some((failure) => /scrolls sideways \(306 px .*\): no single element/.test(failure)), "a pixel over, named even when no one element stands out");
  // Fails closed (review of #957): a measurement not taken, or taken in a wider window, is no evidence.
  assert.ok(journeyVerdict({ ...fits, reflow: { failed: true } }, SEARCH).includes("the page was not measured at 320 CSS pixels"));
  assert.ok(journeyVerdict({ ...fits, reflow: { width: 1265, scrollWidth: 1265, offenders: [] } }, SEARCH).includes("the page was measured in a 1265 px window, not one of at most 320"));
});

/** A home page's run that did what the front door must: its search dated today, its own bytes and its first screen. */
function goodHome(today = todayInLuxembourgAt(Date.now())) {
  const run = goodSearch();
  run.requests[0].url = `${ORIGIN}/en/`;
  run.requests[1].url = `${ORIGIN}/en/client-live-home.js`;
  run.requests[2].postData = JSON.stringify(bodyOn(JOURNEY_STEPS.home.body, today));
  run.location = endOf(JOURNEY_STEPS.home, today);
  run.todays = [today];
  run.frontDoor = { coverageLine: "counted", dateField: today, facts: true };
  run.firstScreen = { height: 640, inputFirst: true, input: 333, statement: 591 };
  return run;
}

const HOME = { origin: ORIGIN, step: JOURNEY_STEPS.home, state: "success", texts: ["4 with the exact phrase"] };

test("the home page's run passes when it arrived rendered, dated today in Luxembourg, with the box and the sentence under it in view", () => {
  assert.equal(JOURNEY_STEPS.home.frontDoor, true);
  assert.deepEqual(JOURNEY_STEPS.home.body.parameters, { query: SEARCH_PHRASE, language: "fra", mode: "strict", date: TODAY_IN_LUXEMBOURG });
  assert.deepEqual(journeyVerdict(goodHome(), HOME), []);
  // A run across midnight in Luxembourg: the page asked on either of its two days.
  const across = goodHome("2026-10-25");
  across.todays = ["2026-10-24", "2026-10-25"];
  assert.deepEqual(journeyVerdict(across, HOME), []);
  assert.equal(todayInLuxembourgAt(Date.parse("2026-10-24T22:30:00Z")), "2026-10-25", "the coded rule: 23:30 in summer time is 00:30 on the 25th");
  assert.deepEqual(frontDoorOf('<p class="coverage-line" data-coverage-line="counted">x</p><input type="text" data-search-date="" aria-describedby="search-today" value="2026-10-16"/>'
    + '<script type="application/json" id="lex-home-facts">{}</script>'), { coverageLine: "counted", dateField: "2026-10-16", facts: true });
  assert.deepEqual(frontDoorOf("<p>nothing</p>"), { coverageLine: null, dateField: null, facts: false });
  assert.equal(NOT_ADVICE_SAID, "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.");
});

test("each way the home page's run can fail is a failure, named", () => {
  const cases = [
    ["another day sent", (o) => { o.requests[2].postData = JSON.stringify(bodyOn(JOURNEY_STEPS.home.body, "2001-01-01")); }, /body was .*2001-01-01.*, not /],
    ["no date sent", (o) => { o.requests[2].postData = JSON.stringify(JOURNEY_STEPS.search.body); }, /body was .*, not /],
    ["the placeholder sent", (o) => { o.requests[2].postData = JSON.stringify(JOURNEY_STEPS.home.body); }, /body was .*today in Luxembourg/],
    ["its own bytes not read", (o) => { delete o.frontDoor; }, /home page's own bytes were not read/],
    ["not rendered by the server", (o) => { o.frontDoor.facts = false; }, /arrived without the facts the server renders it from/],
    ["no coverage line", (o) => { o.frontDoor.coverageLine = null; }, /coverage line is absent, not counted/],
    ["the no-mount line on a mount", (o) => { o.frontDoor.coverageLine = "unmounted"; }, /coverage line is unmounted, not counted/],
    ["another day in the field", (o) => { o.frontDoor.dateField = "2001-01-01"; }, /date field holds "2001-01-01", not today in Luxembourg/],
    ["an empty field", (o) => { o.frontDoor.dateField = ""; }, /date field holds "", not today/],
    ["not measured on a phone", (o) => { delete o.firstScreen; }, /not measured at a small phone's size/],
    ["the box not first", (o) => { o.firstScreen.inputFirst = false; }, /search box is not the first control/],
    ["the box below the fold", (o) => { o.firstScreen.input = 700; }, /search box ends at 700 px, below the first 640 px/],
    ["the sentence below the fold", (o) => { o.firstScreen.statement = 659; }, /sentence under the box ends at 659 px, below the first 640 px/],
    ["no sentence", (o) => { o.firstScreen.statement = null; }, /sentence under the box ends at null px/],
  ];
  for (const [what, mutate, reason] of cases) {
    const observed = goodHome();
    mutate(observed);
    const failures = journeyVerdict(observed, HOME);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
  // With no mount, the home page's line is the no-mount sentence, and a counted one fails.
  const unmounted = { ...HOME, state: "refusal", refusalCode: "no_corpus_mounted" };
  const run = goodHome();
  assert.ok(journeyVerdict(run, unmounted).some((failure) => /coverage line is counted, not unmounted/.test(failure)));
  run.frontDoor.coverageLine = "unmounted";
  assert.ok(!journeyVerdict(run, unmounted).some((failure) => /coverage line/.test(failure)));
});

test("a coverage run is still judged as one, with no body required", () => {
  assert.equal(JOURNEY_STEPS.coverage.body, null);
  // Trust and Coverage is coverage.html; the root is the home page (the search screen).
  assert.deepEqual(journeyVerdict({ ...good(), location: `${ORIGIN}/en/coverage.html` }, SUCCESS), []);
  assert.ok(journeyVerdict({ ...good(), location: `${ORIGIN}/en/coverage.html?x=1` }, SUCCESS).some((failure) => /ended at/.test(failure)));
  assert.ok(journeyVerdict({ ...good(), location: `${ORIGIN}/en/` }, SUCCESS).some((failure) => /ended at/.test(failure)), "the home page is not Trust and Coverage");
});

test("a dossier run is held to its own page, operation and exact body", () => {
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/en/dossier.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/dossier`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.dossier.body) },
  ];
  observed.location = `${ORIGIN}/en/dossier.html`;
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
    { url: `${ORIGIN}/en/reading.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.reading.body) },
  ];
  observed.location = `${ORIGIN}/en/reading.html`;
  observed.text = "49 articles quoted";
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.reading, state: "success", texts: ["49 articles quoted"] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "evidence_bundle", parameters: { identifier: DOSSIER_IDENTIFIER } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was/.test(failure)), "a request that dropped the date fails");
});

test("a work's address and a state's permalink open their pages with nothing typed, each asking one exact body", () => {
  const { address, permalink } = JOURNEY_STEPS;
  assert.equal(address.path, `/en${DOSSIER_IDENTIFIER}`, "the work's address is its coordinate, under the interface language");
  assert.equal(address.typed, undefined);
  assert.deepEqual(address.body, JOURNEY_STEPS.dossier.body);
  assert.equal(permalink.typed, undefined);
  // R64: the page asks the reading by the permalink itself, with no article in it, its date and the text's language it
  // names, for the page that holds the article (the plan's item 3.7).
  assert.deepEqual(permalink.body, { operation_id: "evidence_bundle", parameters: { identifier: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"0".repeat(64)}/fra`, date: READING_DATE, language: READING_LANGUAGE, article: HISTORY_ANCHOR } });
  assert.equal(READING_LANGUAGE, "fra");
  assert.deepEqual(permalink.pins, { identifier: DOSSIER_IDENTIFIER, date: READING_DATE, anchor: HISTORY_ANCHOR, language: READING_LANGUAGE });
  assert.equal(permalink.path, `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${"0".repeat(64)}/fra/${HISTORY_ANCHOR}`, "a placeholder that pins nothing, the article a segment of it");
  const held = journeyStepsFor({ identifier: "/lu-legilux/rgd-2017-12-13-a1050", date: "2018-01-01", anchor: "art_2", language: "deu" });
  assert.equal(held.address.path, "/en/lu-legilux/rgd-2017-12-13-a1050");
  assert.equal(held.permalink.path, `/en/lu-legilux/rgd-2017-12-13-a1050/2018-01-01--${"0".repeat(64)}/deu/art_2`);
  assert.equal(held.permalink.body.parameters.language, "deu");
  // R67: the work's readable address at the article, asked at today's date in Luxembourg, in its language.
  const today = journeyStepsFor({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE, anchor: HISTORY_ANCHOR, today: "2026-10-16" }).current;
  assert.equal(today.path, `/en${DOSSIER_IDENTIFIER}/fra/${HISTORY_ANCHOR}`);
  assert.deepEqual(today.body, { operation_id: "evidence_bundle", parameters: { identifier: DOSSIER_IDENTIFIER, date: "2026-10-16", language: "fra", article: HISTORY_ANCHOR } });
  assert.deepEqual(today.current, { anchor: HISTORY_ANCHOR });
  assert.equal(today.rendered, true);
});

test("a permalink step opens the permalink the server's dossier names for its date, and keeps its placeholder otherwise", async () => {
  const { permalink } = JOURNEY_STEPS;
  const digest = "e".repeat(64);
  const states = [
    { applicability_date: "2020-01-01", language: "fra", permalink: `${DOSSIER_IDENTIFIER}/2020-01-01--${"d".repeat(64)}/fra` },
    { applicability_date: READING_DATE, language: "fra", permalink: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${digest}/fra` },
    { applicability_date: READING_DATE, language: "deu", permalink: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"c".repeat(64)}/deu` },
  ];
  const asked = [];
  const ask = async (origin, operation, parameters) => {
    asked.push([origin, operation, parameters]);
    return { verdict: "answer", result: { value: { states } } };
  };
  const pinned = await pinnedStep(ORIGIN, permalink, { ask });
  assert.deepEqual(asked, [[ORIGIN, "dossier", { identifier: DOSSIER_IDENTIFIER }]], "one dossier, of the same server");
  assert.equal(pinned.path, `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${digest}/fra/${HISTORY_ANCHOR}`, "the state of its language on the date, at the article, under the interface language");
  assert.deepEqual(pinned.body.parameters, { ...permalink.body.parameters, identifier: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${digest}/fra` }, "the page asks by that permalink (R64)");
  assert.deepEqual({ ...pinned, path: permalink.path, body: permalink.body }, { ...permalink }, "nothing else changes");
  assert.equal(await pinnedStep(ORIGIN, JOURNEY_STEPS.reading, { ask }), JOURNEY_STEPS.reading, "a step that pins nothing is left as it is");
  assert.equal(asked.length, 1, "and asks nothing");
  const twoOnTheDate = [...states, { applicability_date: READING_DATE, language: "fra", permalink: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"f".repeat(64)}/fra` }];
  for (const answer of [
    null,
    { verdict: "refuse", refusal: { code: "no_corpus_mounted" } },
    { verdict: "answer", result: { value: { states: [] } } },
    { verdict: "answer", result: { value: { states: twoOnTheDate } } },
    { verdict: "answer", result: { value: { states: [{ applicability_date: READING_DATE, language: "fra", permalink: "/reading.html" }] } } },
    { verdict: "answer", result: { value: { states: [{ applicability_date: READING_DATE, language: "deu", permalink: `${DOSSIER_IDENTIFIER}/${READING_DATE}--${digest}/deu` }] } } },
  ]) {
    assert.equal(await pinnedStep(ORIGIN, permalink, { ask: async () => answer }), permalink, `${JSON.stringify(answer)}: the placeholder stays`);
  }
});

test("an answer's citations are its permalinks printed as code and its links to a reading, under any interface", () => {
  const state = `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra`;
  const wording = `/eu-eurlex/32016R0679/2016-04-27--${"c".repeat(64)}/eng`;
  assert.deepEqual(answerCitations([state, `${state}/art_15`, "e".repeat(64), "https://legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", "art_15"], []), [state, `${state}/art_15`], "code beginning with a slash");
  assert.deepEqual(answerCitations([], [`${state}/art_15`, `/fr${wording}/art_5`, `${wording}/art_5#par_2`, "/reading.html", DOSSIER_IDENTIFIER, "#main", "https://legilux.public.lu/"]), [`${state}/art_15`, `${wording}/art_5`],
    "a link to a state's or a wording's reading, as the permalink it opens (a position inside the article is no part of it); a work's address, a page or another site is no citation");
  assert.deepEqual(answerCitations([state], [state]), [state], "a permalink printed and linked is one citation");
});

test("a permalink run passes when its page shows the pinned state with the article in view, and fails otherwise", () => {
  const step = { ...JOURNEY_STEPS.permalink, path: `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra/${HISTORY_ANCHOR}` };
  const observed = () => ({
    ...goodSearch(),
    requests: [
      { url: `${ORIGIN}${step.path.split("#")[0]}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-reading.js`, method: "GET", headers: {} },
      { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(step.body) },
    ],
    location: endOf(step),
    text: "49 articles quoted",
    pinReplaced: false,
    inView: "shown",
    targetFocused: true,
  });
  const expected = { origin: ORIGIN, step, state: "success", texts: ["49 articles quoted"] };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  for (const [what, change, reason] of [
    ["another version shown", { pinReplaced: true }, /not the one served \(true\)/],
    ["the notice not observed", { pinReplaced: undefined }, /not the one served \(not observed\)/],
    ["the article off-screen", { inView: "at 2400 px of a 600 px window" }, /not in view \(at 2400 px of a 600 px window\)/],
    ["the article missing", { inView: "absent" }, /art_15 the address names is not on the page, first/],
    ["the article not observed", { inView: undefined }, /not in view \(not observed\)/],
    ["the article in view without the focus", { targetFocused: false }, /art_15 the address names is in view without the focus/],
    ["the article dropped from the address", { location: `${ORIGIN}${step.path.slice(0, step.path.lastIndexOf("/"))}` }, /ended at/],
    ["a form submitted as well", { requests: [...observed().requests, observed().requests[2]] }, /2 requests to the API/],
  ]) {
    const failures = journeyVerdict({ ...observed(), ...change }, expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
  const refused = { ...observed(), answerState: "refusal", text: "no_corpus_mounted", pinReplaced: false, inView: "absent" };
  assert.deepEqual(journeyVerdict(refused, { origin: ORIGIN, step, state: "refusal", refusalCode: "no_corpus_mounted" }), [], "a refusal holds no state to show");
});

test("a permalink run checks the cited article's evidence form: verify confirms its permalink and its text digest (R65)", async () => {
  const step = { ...JOURNEY_STEPS.permalink, path: `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra/${HISTORY_ANCHOR}` };
  const permalink = `https://law.soufien.lu${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra/${HISTORY_ANCHOR}`;
  const digest = "a".repeat(64);
  const observed = (change = {}) => ({
    ...goodSearch(),
    requests: [
      { url: `${ORIGIN}${step.path.split("#")[0]}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-reading.js`, method: "GET", headers: {} },
      { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(step.body) },
    ],
    location: endOf(step),
    text: "49 articles quoted",
    pinReplaced: false,
    inView: "shown",
    targetFocused: true,
    citedEvidence: [{ permalink, textSha256: digest }],
    evidenceVerifications: [{ identifier: permalink, textSha256: digest, refusal: null, verdict: "digest_matches", articleTextSha256: digest }],
    ...change,
  });
  const expected = { origin: ORIGIN, step, state: "success", texts: ["49 articles quoted"] };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  const check = observed().evidenceVerifications[0];
  for (const [what, change, reason] of [
    ["no evidence form", { citedEvidence: [], evidenceVerifications: undefined }, /carries no evidence form/],
    ["not verified", { evidenceVerifications: undefined }, /1 evidence forms were not verified/],
    ["refused", { evidenceVerifications: [{ ...check, refusal: "pinned_digest_mismatch", verdict: null }] }, /refused the evidence form's permalink .* pinned_digest_mismatch/],
    ["another text", { evidenceVerifications: [{ ...check, articleTextSha256: "b".repeat(64) }] }, /names the text digest b{64} .* carries a{64}/],
  ]) {
    const failures = journeyVerdict(observed(change), expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
  // The check asks verify by the permalink as printed, and keeps what it names beside the form's digest.
  const asked = [];
  const answers = await verifyEvidence(ORIGIN, [{ permalink, textSha256: digest }], async (url, init) => {
    asked.push([url, JSON.parse(init.body)]);
    return { status: 200, json: async () => ({ result: { value: { verdict: "digest_matches", article_text_sha256: digest } }, refusal: null }) };
  });
  assert.deepEqual(asked, [[`${ORIGIN}/api/v3/verify`, { operation_id: "verify", parameters: { identifier: permalink } }]]);
  assert.deepEqual(answers, [check]);
});

test("a stale permalink run passes when it is refused, and its page says, in view, that the link pins no version held, with the current link", () => {
  const step = JOURNEY_STEPS.stale;
  assert.equal(step.path, `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${"0".repeat(64)}/fra/${HISTORY_ANCHOR}`, "a digest no state has");
  assert.equal(step.pins, undefined, "opened as it is, never replaced by the dossier's permalink");
  assert.deepEqual(step.body, JOURNEY_STEPS.permalink.body);
  const current = `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra`;
  const observed = () => ({
    ...goodSearch(),
    answerState: "refusal",
    refusalCodes: ["pinned_digest_mismatch"],
    requests: [
      { url: `${ORIGIN}${step.path.split("#")[0]}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(step.body) },
    ],
    location: endOf(step),
    text: "This link pins …, which is not the version this site holds for that date; the version it holds is … pinned_digest_mismatch",
    pinReplaced: false,
    pinNotHeld: true,
    pinCurrent: `/en${current}`,
    inView: "shown",
    targetFocused: true,
    pinNamed: step.path.slice("/en".length, step.path.lastIndexOf("/")),
    pinVerification: { identifier: step.path.slice("/en".length, step.path.lastIndexOf("/")), refusal: "pinned_digest_mismatch", verdict: null },
  });
  const expected = { origin: ORIGIN, step, state: "refusal", refusalCode: "pinned_digest_mismatch", texts: ["which is not the version this site holds for that date"] };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  for (const [what, change, reason] of [
    ["no notice", { pinNotHeld: false }, /does not say that the link pins no version held at its date \(false\)/],
    ["the notice not observed", { pinNotHeld: undefined }, /does not say .* \(not observed\)/],
    ["the notice scrolled past", { inView: "at -1180 px of a 844 px window" }, /notice .* is not in view \(at -1180 px/],
    ["the notice in view without the focus", { targetFocused: false }, /notice .* is in view without the focus/],
    ["no current link", { pinCurrent: null }, /the notice links no current version \(none\)/],
    ["a current link to a work, not a version", { pinCurrent: `/en${DOSSIER_IDENTIFIER}` }, /the notice links no current version/],
    ["a notice naming the version held", { pinVerification: { refusal: null, verdict: "digest_matches" } }, /with digest_matches, not pinned_digest_mismatch/],
    ["a notice verify was not asked about", { pinNamed: null, pinVerification: undefined }, /\(none\) with nothing, not pinned_digest_mismatch/],
    ["another version shown in its place", { answerState: "success" }, /ended in success, not refusal/],
  ]) {
    const failures = journeyVerdict({ ...observed(), ...change }, expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
});

test("a readable address run passes when its page shows the article it names first, in view and focused (R67)", () => {
  const step = JOURNEY_STEPS.current;
  const observed = () => ({
    ...goodSearch(),
    requests: [
      { url: `${ORIGIN}${step.path}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-reading.js`, method: "GET", headers: {} },
    ],
    location: endOf(step),
    text: "Quoted from loi-1991-08-10-n3, fra, the state applying from 2024-02-01. 49 articles quoted",
    inView: "shown",
    targetFocused: true,
    serverRendered: true,
    withoutScripts: { answerState: "success" },
  });
  const expected = { origin: ORIGIN, step, state: "success", texts: ["Quoted from loi-1991-08-10-n3"], mustRender: true };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  for (const [what, change, reason] of [
    ["the article not first", { inView: "absent" }, /art_15 the address names is not on the page, first/],
    ["the article without the focus", { targetFocused: false }, /in view without the focus/],
  ]) {
    const failures = journeyVerdict({ ...observed(), ...change }, expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
});

test("a page at a law's address the server renders asks nothing as it loads and shows its answer with no script (R67)", () => {
  for (const name of ["address", "permalink", "stale", "current"]) assert.equal(JOURNEY_STEPS[name].rendered, true, `${name} is rendered by the server`);
  assert.equal(JOURNEY_STEPS.reading.rendered, undefined, "a page the reader asks from its form is not");
  const step = { ...JOURNEY_STEPS.permalink, path: `/en${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra/${HISTORY_ANCHOR}` };
  const observed = () => ({
    ...goodSearch(),
    requests: [
      { url: `${ORIGIN}${step.path.split("#")[0]}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/en/client-live-reading.js`, method: "GET", headers: {} },
    ],
    location: endOf(step),
    text: "49 articles quoted",
    pinReplaced: false,
    inView: "shown",
    targetFocused: true,
    serverRendered: true,
    withoutScripts: { answerState: "success" },
  });
  const expected = { origin: ORIGIN, step, state: "success", texts: ["49 articles quoted"], mustRender: true };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  const askOnce = { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(step.body) };
  for (const [what, change, reason] of [
    ["the page arrived as built", { serverRendered: false }, /arrived without the answer the server renders it from/],
    ["it asked anyway", { requests: [...observed().requests, askOnce] }, /made 1 requests to the API, not exactly none/],
    ["no answer without scripts", { withoutScripts: { answerState: null } }, /without scripts the page shows no answer, not a success/],
    ["the idle form without scripts", { withoutScripts: { answerState: "idle" } }, /without scripts the page shows a idle, not a success/],
    ["not looked at without scripts", { withoutScripts: undefined }, /without scripts the page shows no answer/],
  ]) {
    const failures = journeyVerdict({ ...observed(), ...change }, expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }
  // A step that does not require a rendered page (`mustRender` unset): a page that asks once, as any page does, passes.
  const unrendered = { ...observed(), serverRendered: false, withoutScripts: undefined, answerState: "refusal", text: "no_corpus_mounted", requests: [...observed().requests, askOnce] };
  assert.deepEqual(journeyVerdict(unrendered, { origin: ORIGIN, step, state: "refusal", refusalCode: "no_corpus_mounted" }), []);
});

test("a law's page names as canonical the address it shows in its own interface language, each language and x-default as alternates (R61, R64, R67)", () => {
  const site = "https://law.soufien.lu";
  const article = `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra/${HISTORY_ANCHOR}`;
  const step = { ...JOURNEY_STEPS.permalink, path: `/en${article}` };
  const links = (address, locale = "en") => ({ canonical: `${site}/${locale}${address}`, alternates: [["fr", `${site}/fr${address}`], ["en", `${site}/en${address}`], ["x-default", `${site}${address}`]] });
  const observed = (change = {}) => ({
    ...goodSearch(),
    requests: [{ url: `${ORIGIN}${step.path}`, method: "GET", headers: {} }],
    location: endOf(step),
    text: "49 articles quoted",
    pinReplaced: false,
    inView: "shown",
    targetFocused: true,
    serverRendered: true,
    withoutScripts: { answerState: "success" },
    ...links(article),
    ...change,
  });
  const expected = { origin: ORIGIN, step, state: "success", texts: ["49 articles quoted"], mustRender: true };
  assert.deepEqual(journeyVerdict(observed(), expected), []);
  // A permalink of the version its act's readable address shows today names that readable address, the article's (R67).
  const readable = `${DOSSIER_IDENTIFIER}/fra/${HISTORY_ANCHOR}`;
  assert.deepEqual(journeyVerdict(observed(links(readable)), expected), [], "today's version: its readable address");
  // A page under /fr/ names its canonical under /fr/.
  const french = { ...links(article, "fr"), location: `${ORIGIN}/fr${article}`, requests: [{ url: `${ORIGIN}/fr${article}`, method: "GET", headers: {} }] };
  assert.deepEqual(journeyVerdict(observed(french), { ...expected, step: { ...step, path: `/fr${article}` } }), [], "the French page");
  for (const [what, change, reason] of [
    ["no canonical link", { canonical: null }, /names no canonical link/],
    ["no interface language", { canonical: `${site}${article}` }, /not a law's address on https:\/\/law\.soufien\.lu\/en, the page's own interface language/],
    ["the other interface language", { canonical: `${site}/fr${article}` }, /the page's own interface language/],
    ["the state without its article", links(article.slice(0, article.lastIndexOf("/"))), /canonical, not https:\/\/law\.soufien\.lu\/en\/lu-legilux/],
    ["another article's readable address", links(`${DOSSIER_IDENTIFIER}/fra/art_1`), /canonical, not https:\/\/law\.soufien\.lu\/en\/lu-legilux/],
    ["another origin", { canonical: `${ORIGIN}/en${article}` }, /not a law's address on https:\/\/law\.soufien\.lu/],
    ["no x-default", { alternates: [["fr", `${site}/fr${article}`], ["en", `${site}/en${article}`]] }, /alternates are/],
    // Every link is counted (review of #967): one named twice is a failure, which a map of them would hide.
    ["an alternate twice", { alternates: [...links(article).alternates, ["en", `${site}/en${article}`]] }, /alternates are .*, each once/],
    ["two canonical links", { canonical: [`${site}/en${article}`, `${site}/en${article}`] }, /not a law's address/],
  ]) {
    const failures = journeyVerdict(observed(change), expected);
    assert.ok(failures.some((failure) => reason.test(failure)), `${what}: ${failures.join(" | ")}`);
  }

  // A stale permalink's page names the current version the refusal links (R64), never the digest it was opened at.
  const stale = JOURNEY_STEPS.stale;
  const shown = `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"e".repeat(64)}/fra`;
  const staleObserved = (address) => ({
    ...observed(links(address)), answerState: "refusal", refusalCodes: ["pinned_digest_mismatch"], location: `${ORIGIN}${stale.path}`,
    text: "pinned_digest_mismatch", pinNotHeld: true, pinCurrent: `/en${shown}`, pinNamed: stale.path.slice("/en".length, stale.path.lastIndexOf("/")),
    pinVerification: { identifier: shown, refusal: "pinned_digest_mismatch", verdict: null },
  });
  const staleExpected = { origin: ORIGIN, step: stale, state: "refusal", refusalCode: "pinned_digest_mismatch", mustRender: true, withoutScripts: undefined };
  assert.deepEqual(journeyVerdict({ ...staleObserved(shown), withoutScripts: { answerState: "refusal" } }, staleExpected), []);
  assert.ok(journeyVerdict(staleObserved(stale.path.slice("/en".length)), staleExpected).some((failure) => /canonical, not the version it shows/.test(failure)));
  // The version it links, with no article: the article the stale address names is not the answer's to mint (review of #967).
  assert.ok(journeyVerdict(staleObserved(`${shown}/${HISTORY_ANCHOR}`), staleExpected).some((failure) => /canonical, not the version it shows/.test(failure)));
});

test("a provision history run types the identifier and the article id and is held to both in the body", () => {
  assert.deepEqual(JOURNEY_STEPS.history.typed, [DOSSIER_IDENTIFIER, HISTORY_ANCHOR]);
  assert.deepEqual(JOURNEY_STEPS.history.body, { operation_id: "article_history", parameters: { identifier: DOSSIER_IDENTIFIER, anchor: HISTORY_ANCHOR } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/en/history.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/article_history`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.history.body) },
  ];
  observed.location = `${ORIGIN}/en/history.html`;
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
    { url: `${ORIGIN}/en/compare.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/diff`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.compare.body) },
  ];
  observed.location = `${ORIGIN}/en/compare.html`;
  observed.text = `Both dates select the one text held, the act as published, applicable from ${READING_DATE} (publisher); later versions are not held.`;
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.compare, state: "success", texts: [`Both dates select the one text held, the act as published, applicable from ${READING_DATE} (publisher); later versions are not held.`] };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  observed.requests[1].postData = JSON.stringify({ operation_id: "diff", parameters: { identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: "2025-01-01" } });
  assert.ok(journeyVerdict(observed, expected).some((failure) => /body was/.test(failure)), "a request with another date fails");
});

test("a radar run types two dates and is held to exactly those in the body", () => {
  assert.deepEqual(JOURNEY_STEPS.radar.typed, [READING_DATE, READING_DATE]);
  assert.deepEqual(JOURNEY_STEPS.radar.body, { operation_id: "changes_in_period", parameters: { date_from: READING_DATE, date_to: READING_DATE } });
  const observed = goodSearch();
  observed.requests = [
    { url: `${ORIGIN}/en/radar.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/changes_in_period`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.radar.body) },
  ];
  observed.location = `${ORIGIN}/en/radar.html`;
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
    { url: `${ORIGIN}/en/export.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(JOURNEY_STEPS.export.body) },
  ];
  observed.location = `${ORIGIN}/en/export.html`;
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

test("a file the API writes in its temporary folder while serving fails the run, named there (2026-10-13)", async () => {
  // The population mount's API wrote SQLite temporary files (etilqs_*) there; only the image's probe, which watches the
  // container's /tmp, saw them.
  const { mkdtemp, rm, unlink, writeFile } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const home = await mkdtemp(join(tmpdir(), "lex-journey-watch-"));
  const temporary = await mkdtemp(join(tmpdir(), "lex-journey-watch-tmp-"));
  try {
    const observed = goodSearch();
    const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
    const quiet = watchApiFiles(home, temporary);
    assert.equal(quiet.stop(), quiet.stop(), "the run and the API's close get one stop");
    assert.deepEqual(journeyVerdict({ ...observed, api: { output: "", changedFiles: [], fileEvents: await quiet.stop() } }, expected), [], "untouched folders pass");
    const watched = watchApiFiles(home, temporary);
    await writeFile(join(temporary, "etilqs_5f840f00463e0ba0"), "a spilled sort");
    await unlink(join(temporary, "etilqs_5f840f00463e0ba0"));
    const failures = journeyVerdict({ ...observed, api: { output: "", changedFiles: [], fileEvents: await watched.stop() } }, expected);
    assert.ok(failures.some((failure) => failure.startsWith("the API process touched files while serving the run") && failure.includes("TMP/etilqs_5f840f00463e0ba0")), failures.join("; "));
  } finally {
    await rm(home, { recursive: true, force: true });
    await rm(temporary, { recursive: true, force: true });
  }
});

test("a file watch that failed fails the run with its reason, never as a file the API touched", () => {
  const observed = goodSearch();
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const message = "ENOENT: no such file or directory, scandir '/tmp/lex-journey-api-x/runtimes/linux-musl-arm64'";
  const failures = journeyVerdict({ ...observed, api: { output: "", changedFiles: [], fileEvents: [["error", message]] } }, expected);
  assert.deepEqual(failures, [`the file watch failed, so the run cannot say the API touched no file: ${message}`],
    "a failed watch is said as a failed watch, not as a file the API touched");
});

test("a watch that fails is recorded as an event, not thrown, and its stop is one promise however often it is asked (review of #921)", async () => {
  // The watch the journeys hold is replaced by one this test fails at will: the base's push run of 67bae40e died of
  // the error this emits, unhandled, when a recursive watch on Linux walked a directory being removed.
  const fs = await import("node:fs");
  const { syncBuiltinESMExports } = await import("node:module");
  const { EventEmitter } = await import("node:events");
  const original = fs.default.watch;
  const failing = Object.assign(new EventEmitter(), { close() {} });
  fs.default.watch = () => failing;
  syncBuiltinESMExports();
  try {
    const watched = watchFiles("a directory the stub never reads");
    failing.emit("error", new Error("ENOENT: no such file or directory, scandir"));
    assert.equal(watched.stop(), watched.stop(), "the run and the API's close get one stop, not two");
    assert.deepEqual(await watched.stop(), [["error", "ENOENT: no such file or directory, scandir"]]);
  } finally {
    fs.default.watch = original;
    syncBuiltinESMExports();
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
  const state = `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${digest}/fra`;
  const article = `${state}/art_15`;
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
  const state = `/lu-legilux/loi-1991-08-10-n3/2024-02-01--${digest}/fra`;
  const source = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo";
  const quoted = (article, codes = [digest, "b".repeat(64), source, `${state}/${article}`]) => ({ article, codes });
  const observed = { ...goodSearch(), quotes: [quoted("art_15"), quoted("art_16")] };
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const failing = (quotes) => journeyVerdict({ ...observed, quotes }, expected);
  assert.deepEqual(journeyVerdict(observed, expected), [], "quotes that carry all four pass");
  assert.ok(failing([quoted("art_15"), quoted("art_16", [digest, "b".repeat(64), source])])
    .includes("the quote of art_16 carries no citation that pins it"), "a quote cited only by its state fails (the export composer's 49 quotes)");
  assert.ok(failing([quoted("art_15"), quoted("art_16", [digest, "b".repeat(64), source, `${state}/art_15`])])
    .includes("the quote of art_16 carries no citation that pins it"), "a citation of another article does not cite this one");
  assert.ok(failing([quoted("art_15", [digest, source, `${state}/art_15`])])
    .includes("the quote of art_15 does not show its text digest, body digest and official source"), "one digest is not two");
  assert.ok(failing([quoted("art_15", [digest, "b".repeat(64), `${state}/art_15`])])
    .includes("the quote of art_15 does not show its text digest, body digest and official source"), "no official source");
});

test("an EU quotation is pinned by its EU wording permalink, its provision escaped as the platform escapes it (review of #903)", () => {
  const digest = "c".repeat(64);
  const wording = `/eu-eurlex/32016R0679/2016-04-27--${digest}/eng`;
  const source = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1.0006.02/DOC_1";
  const quoted = (article, codes) => ({ article, codes });
  const expected = { origin: ORIGIN, step: JOURNEY_STEPS.search, state: "success" };
  const verdict = (quotes) => journeyVerdict({ ...goodSearch(), quotes }, expected);
  assert.deepEqual(verdict([quoted("001", [digest, "d".repeat(64), source, `${wording}/001`])]), [], "an EU quote with its own provision permalink passes");
  assert.deepEqual(verdict([quoted("art 1", [digest, "d".repeat(64), source, `${wording}/art%201`])]), [], "a provision id is compared unescaped");
  assert.ok(verdict([quoted("002", [digest, "d".repeat(64), source, `${wording}/001`])])
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
      "X-Robots-Tag": "noindex",
    },
  });
  const expected = { ...SEARCH, servedByApi: true };
  assert.deepEqual(journeyVerdict(served(), expected), []);
  const cases = [
    ["no frame-ancestors", (o) => { o.pageHeaders["Content-Security-Policy"] = cspValue(); }, /arrived with the CSP/],
    ["no HSTS", (o) => { delete o.pageHeaders["Strict-Transport-Security"]; }, /without HSTS/],
    ["a referrer policy that sends one", (o) => { o.pageHeaders["Referrer-Policy"] = "origin"; }, /Referrer-Policy origin/],
    ["no nosniff", (o) => { delete o.pageHeaders["X-Content-Type-Options"]; }, /without nosniff/],
    // Kept out of search engines until the owner answers O27 (R67).
    ["no X-Robots-Tag", (o) => { delete o.pageHeaders["X-Robots-Tag"]; }, /X-Robots-Tag null, not noindex/],
    ["no robots element", (o) => { o.robots = []; }, /robots elements \[\], not one noindex/],
    ["two robots elements", (o) => { o.robots = ["noindex", "noindex"]; }, /not one noindex/],
    ["a page opened to search engines", (o) => { o.robots = ["index"]; }, /not kept out of search engines/],
    ["headers never observed", (o) => { o.pageHeaders = null; }, /arrived with the CSP null/],
  ];
  for (const [what, mutate, reason] of cases) {
    const observed = served();
    mutate(observed);
    assert.ok(journeyVerdict(observed, expected).some((failure) => reason.test(failure)), what);
  }
  assert.deepEqual(journeyVerdict({ ...goodSearch(), pageHeaders: null }, SEARCH), [], "a page served by serve-live is not held to them");
  // A run against a release opened to search engines (O27 answered yes) is not held to noindex: its record decides.
  const opened = served();
  delete opened.pageHeaders["X-Robots-Tag"];
  assert.deepEqual(journeyVerdict({ ...opened, robots: [] }, { ...expected, searchEngines: "indexed" }), []);
});

test("a page ends at its own address with what it writes there: a search after #, a readable address's date (plan 3.4, 3.5)", () => {
  const phrase = "#q=assembl%C3%A9e%20g%C3%A9n%C3%A9rale&lang=fra";
  assert.deepEqual(endAddressesOf(JOURNEY_STEPS.search, ["2026-10-18"]), [`/en/search.html${phrase}`], "the search kept after #, undated");
  // The home page's search is dated today: either of a run's two days, across midnight in Luxembourg.
  assert.deepEqual(endAddressesOf(JOURNEY_STEPS.home, ["2026-10-24", "2026-10-25"]), [`/en/${phrase}&date=2026-10-24`, `/en/${phrase}&date=2026-10-25`]);
  assert.deepEqual(endAddressesOf(EU_SEARCH_STEP, ["2026-10-18"]), ["/en/search.html#q=personal%20data&lang=eng&work=32016R0679"], "the work named");
  // The 404 page's box keeps nothing; a page opened at a kept search stays there.
  assert.deepEqual(endAddressesOf(NOT_FOUND_STEP, ["2026-10-18"]), ["/en/no-such-page"]);
  assert.deepEqual(endAddressesOf(KEPT_SEARCH_STEP, ["2026-10-18"]), [`/en/search.html${phrase}`]);
  // An act's readable address ends with the date it read the act at; one that keeps a date keeps it; a permalink names its own.
  assert.deepEqual(endAddressesOf(JOURNEY_STEPS.current, ["2026-10-18"]), [`${JOURNEY_STEPS.current.path}?date=2026-10-18`]);
  const kept = { ...JOURNEY_STEPS.current, path: `${JOURNEY_STEPS.current.path}?date=2024-02-01`, body: { ...JOURNEY_STEPS.current.body, parameters: { ...JOURNEY_STEPS.current.body.parameters, date: "2024-02-01" } } };
  assert.deepEqual(endAddressesOf(kept, ["2026-10-18"]), [kept.path]);
  const paged = { ...JOURNEY_STEPS.current, path: "/en/lu-legilux/loi-1991-08-10-n3/fra?page=2" };
  assert.deepEqual(endAddressesOf(paged, ["2026-10-18"]), ["/en/lu-legilux/loi-1991-08-10-n3/fra?page=2&date=2026-10-18"]);
  for (const step of [JOURNEY_STEPS.permalink, JOURNEY_STEPS.dossier, JOURNEY_STEPS.reading, JOURNEY_STEPS.coverage]) {
    assert.deepEqual(endAddressesOf(step, ["2026-10-18"]), [step.path], step.path);
  }
  // The kept search's run is one request, its body the fragment's search, ending where it began.
  const [[name, expected]] = addressExpectations();
  assert.equal(name, "a search kept in the address");
  assert.equal(expected.step, KEPT_SEARCH_STEP);
  assert.equal(KEPT_SEARCH_STEP.typed, undefined, "nothing typed");
  const observed = { ...goodSearch(), location: `${ORIGIN}${KEPT_SEARCH_STEP.path}` };
  assert.deepEqual(journeyVerdict(observed, { origin: ORIGIN, ...expected, texts: ["4 with the exact phrase"] }), []);
  assert.ok(journeyVerdict({ ...observed, location: `${ORIGIN}/en/search.html` }, { origin: ORIGIN, ...expected, texts: [] }).some((failure) => /ended at/.test(failure)), "the fragment dropped");
});

test("on the fixture mount every step must answer, and coverage must name the mount's own digests", () => {
  const mount = { corpus_sha256: "a".repeat(64), index_sha256: "b".repeat(64), work_key: "lu-legilux/loi-1991-08-10-n3" };
  const expectations = fixtureMountExpectations(mount);
  assert.deepEqual(expectations.map(([name]) => name).sort(), Object.keys(JOURNEY_STEPS).sort(), "each of the eight steps, once");
  assert.ok(expectations.every(([name, expected]) => expected.step === JOURNEY_STEPS[name] && expected.state === (name === "stale" ? "refusal" : "success")), "each is its own step, answered");
  assert.equal(expectations.find(([name]) => name === "stale")[1].refusalCode, "pinned_digest_mismatch", "a stale permalink is refused with the current link (R64)");
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
  assert.deepEqual(EU_SEARCH_STEP.body, { operation_id: "search", parameters: { query: "personal data", language: "eng", mode: "strict", identifier: "32016R0679" } },
    "in the order the page sends them: the phrase, the language, the strict lane, the work");
  assert.equal(EU_SEARCH_STEP.path, JOURNEY_STEPS.search.path, "the same search page");
  assert.ok(!Object.values(JOURNEY_STEPS).includes(EU_SEARCH_STEP), "not one of the eight steps every mount runs: it needs an EU index");

  const report = { corpus: { Sha256: "a".repeat(64) }, luxembourgIndex: { Sha256: "b".repeat(64) } };
  const specification = ["J1, a date before the history", "J2, a question no article carries", "J5, a law not held"];
  assert.deepEqual(realMountSteps(report).map(([name]) => name), [...Object.keys(JOURNEY_STEPS), ...specification],
    "a real mount without an EU index runs the eight, then J2 and J5");
  const withEurope = realMountSteps({ ...report, europeIndex: { Sha256: "c".repeat(64) } });
  assert.deepEqual(withEurope.map(([name]) => name), [...Object.keys(JOURNEY_STEPS), ...specification, "eu search", "eu dossier", "eu reading"],
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
  const wording = `/eu-eurlex/32016R0679/2016-04-27--${digest}/eng`;
  const article = `${wording}/026`;
  const escaped = `${wording}/26%28a%29`;
  assert.deepEqual(pinnedCitation(article), { publisher: "eu-eurlex", digest, anchor: "026" });
  assert.deepEqual(pinnedCitation(escaped), { publisher: "eu-eurlex", digest, anchor: "26(a)" }, "verify names the provision unescaped");
  assert.deepEqual(pinnedCitation(wording), { publisher: "eu-eurlex", digest, anchor: null });
  assert.equal(pinnedCitation(`${wording}/%E0%A4%A`), null, "a provision that is not a valid escape pins nothing");
  assert.equal(pinnedCitation("/eu-eurlex/32016R0679/eng/2016-04-27"), null, "a permalink without its digest pins nothing");
  assert.equal(pinnedCitation(`/eu-eurlex/32016R0679/eng/2016-04-27--${digest}`), null, "nor does the shape before R64");
  assert.equal(pinnedCitation(`${wording}#026`), null, "nor a provision after #, which R64 leaves for a position inside it");
  assert.equal(pinnedCitation(`http://publications.europa.eu/resource/cellar/x.0006#lex-provision=026`), null, "nor does the provision coordinate");

  const matches = (identifier, anchor = null) => ({ identifier, refusal: null, verdict: "digest_matches", publisher: "eu-eurlex", stateSha256: null, wordingSha256: digest, requestedAnchor: anchor });
  const step = EU_SEARCH_STEP;
  const observed = {
    ...goodSearch(),
    requests: goodSearch().requests.map((request) => (request.postData ? { ...request, postData: JSON.stringify(step.body) } : request)),
    location: endOf(step),
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
    location: endOf(step),
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
    { url: `${ORIGIN}/en/search.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/search`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(NO_HIT_SEARCH_STEP.body) },
  ];
  observed.location = endOf(NO_HIT_SEARCH_STEP);
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
    // A home page's search names today, which the run fills in (`bodyOn`).
    assert.ok(keys.includes(JSON.stringify(bodyOn(step.body ?? { operation_id: "coverage", parameters: {} }, todayInLuxembourgAt(Date.now())))), `${step.path} ${step.operation}`);
  }
  assert.ok(!keys.some((key) => key.includes(TODAY_IN_LUXEMBOURG)), "no request asks the placeholder itself");
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
  assert.equal(exporting.step.path, "/en/export.html");
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
    { url: `${ORIGIN}/en/export.html`, method: "GET", headers: {} },
    { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(exporting.step.body) },
  ];
  observed.location = `${ORIGIN}/en/export.html`;
  observed.text = exporting.texts.join("\n");
  observed.html = '<details><summary>The JSON as it will be saved</summary><pre>{"annexes_not_served": [{"annexes": 1}]}</pre></details>';
  observed.then = "reached";
  const expected = { origin: ORIGIN, ...exporting };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, html: '<pre>{"text": "Hambali"}</pre>' }, expected).includes('the page\'s markup carries withheld text "Hambali"'), "the annex's text in the JSON shown");
  assert.ok(journeyVerdict({ ...observed, then: "clicked" }, expected).some((failure) => /never showed \[data-export-state=composed\] \[data-export-annexes\]/.test(failure)), "no annex listed");
  assert.ok(journeyVerdict({ ...observed, text: observed.text.replace("Save as PDF", "") }, expected).includes('the page does not show "Save as PDF"'), "no PDF offered");
});

test("on a real mount J5 must refuse identifier_unknown with the absence note whatever the API answers, and J2 says the note only when nothing was found", () => {
  const steps = Object.fromEntries(realMountSteps({ corpus: {}, luxembourgIndex: {} }).map(([name, step]) => [name.slice(0, 2), step]));
  const j5 = realMountExpectations(steps.J5);
  assert.equal(steps.J5, UNKNOWN_LAW_STEP);
  for (const envelope of [{ verdict: "refuse", refusal: { code: "identifier_unknown" } }, { verdict: "answer", result: { value: {} } }]) {
    assert.deepEqual(j5.refine(envelope), { state: "refusal", refusalCode: "identifier_unknown", texts: [ABSENCE_NOTE] },
      "an answer to a law the corpus does not hold would be the defect, so the page is held to the refusal regardless");
  }
  const j2 = realMountExpectations(steps.J2);
  assert.equal(steps.J2, NO_HIT_SEARCH_STEP);
  assert.deepEqual(j2.refine({ verdict: "answer", result: { value: { hits: [] } } }), { texts: [ABSENCE_NOTE] });
  assert.deepEqual(j2.refine({ verdict: "answer", result: { value: { hits: [{ id: 1 }] } } }), {},
    "a population that does carry the words answers hits, held like any search");
  assert.deepEqual(realMountExpectations(JOURNEY_STEPS.search), {}, "the eight steps are held to the API's outcome alone");

  // J1 on a real mount: the fixture's work on a date before any state a population could hold.
  assert.equal(steps.J1, PRE_HISTORY_READING_STEP);
  assert.deepEqual(PRE_HISTORY_READING_STEP.body.parameters, { identifier: DOSSIER_IDENTIFIER, date: PRE_HISTORY_DATE });
  assert.deepEqual(PRE_HISTORY_READING_STEP.typed, [DOSSIER_IDENTIFIER, PRE_HISTORY_DATE]);
  assert.equal(PRE_HISTORY_READING_STEP.path, "/en/reading.html");
  assert.deepEqual(realMountExpectations(steps.J1).refine({ verdict: "answer", result: { value: {} } }),
    { state: "refusal", refusalCode: "no_version_for_date", texts: [NO_EARLIER_STATE] }, "another date's text would be the defect");
});

test("on a real mount the steps ask for a Luxembourg work the mount holds, and J1 for the day before its history", () => {
  // The fixture's work is not in a population: on the population mount its steps met only identifier_unknown (2026-10-13).
  const held = { identifier: "/lu-legilux/loi-2015-12-03-n1", date: "2015-12-11", anchor: "art_1er", language: "fra", beforeHistory: "2015-12-10" };
  const report = { corpus: {}, luxembourgIndex: {} };
  const steps = Object.fromEntries(realMountSteps(report, held));
  assert.deepEqual(steps.dossier.body.parameters, { identifier: held.identifier });
  assert.deepEqual(steps.reading.body.parameters, { identifier: held.identifier, date: held.date });
  assert.deepEqual(steps.export.typed, [held.identifier, held.date]);
  assert.deepEqual(steps.compare.typed, [held.identifier, held.date, held.date]);
  assert.deepEqual(steps.radar.body.parameters, { date_from: held.date, date_to: held.date });
  assert.deepEqual(steps.history.body.parameters, { identifier: held.identifier, anchor: held.anchor });
  assert.deepEqual(steps.search, JOURNEY_STEPS.search, "the search phrase is the same");
  const j1 = steps["J1, a date before the history"];
  assert.deepEqual(j1.body.parameters, { identifier: held.identifier, date: held.beforeHistory });
  assert.deepEqual(j1.typed, [held.identifier, held.beforeHistory]);
  assert.deepEqual(realMountExpectations(j1).refine({ verdict: "answer", result: { value: {} } }),
    { state: "refusal", refusalCode: "no_version_for_date", texts: [NO_EARLIER_STATE] }, "held to the refusal as the fixture's J1 is");
  // Without a held work, the fixture's steps, unchanged; and the fixture's steps are the same steps built for its work.
  assert.deepEqual(realMountSteps(report, null), realMountSteps(report));
  assert.deepEqual(journeyStepsFor({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE, anchor: HISTORY_ANCHOR }), JOURNEY_STEPS);
});

test("a revision's held work is chosen from its answers as from an index: three hits in a state alone on its date", async () => {
  const hit = (work, date, state, publisher_id) => ({ work_key: work, applicability_date: date, state_sha256: state, publisher_id, language: "fra" });
  const answers = {
    search: { result: { value: { hits: [
      hit("a-few", "2001-01-01", "s1", "art_1"), hit("a-few", "2001-01-01", "s1", "art_2"),
      hit("b-twin", "2002-02-02", "s2", "art_1"), hit("b-twin", "2002-02-02", "s2", "art_2"), hit("b-twin", "2002-02-02", "s2", "art_3"),
      hit("c-held", "2010-05-05", "s3", "art_9"), hit("c-held", "2010-05-05", "s3", "art_10"), hit("c-held", "2010-05-05", "s3", "art_10"),
      hit("c-held", "2010-05-05", "s3", "art_2"),
    ] } } },
    "/lu-legilux/b-twin": { result: { value: { states: [{ applicability_date: "2002-02-02" }, { applicability_date: "2002-02-02" }] } } },
    "/lu-legilux/c-held": { result: { value: { states: [{ applicability_date: "2010-05-05" }, { applicability_date: "2008-03-01" }] } } },
  };
  const asked = [];
  const ask = async (origin, operation, parameters) => {
    asked.push([operation, parameters]);
    return operation === "search" ? answers.search : answers[parameters.identifier];
  };
  assert.deepEqual(await heldLuxembourgWorkFromAnswers("http://revision", { ask }),
    { identifier: "/lu-legilux/c-held", date: "2010-05-05", anchor: "art_10", language: "fra", beforeHistory: "2008-02-29" },
    "two hits are too few, twins on a date are refused, and J1 is the day before the work's first state");
  assert.deepEqual(asked[0], ["search", { query: "loi", language: "fra" }]);
  assert.deepEqual(asked.slice(1).map(([operation, parameters]) => [operation, parameters.identifier]),
    [["dossier", "/lu-legilux/b-twin"], ["dossier", "/lu-legilux/c-held"]], "a state with too few hits is not asked about");
  assert.equal(await heldLuxembourgWorkFromAnswers("http://revision", { ask: async () => null }), null, "no answer, no work: the fixture's is asked");
});

test("a page's expectation is the API's outcome, then the step's own refinement of it, which wins", () => {
  const answered = { verdict: "answer", result: { value: { hits: [] } } };
  assert.deepEqual(expectedForAnswer({ step: NO_HIT_SEARCH_STEP }, answered), { step: NO_HIT_SEARCH_STEP, state: "success", nothingToCite: true });
  const j5 = { step: UNKNOWN_LAW_STEP, ...realMountExpectations(UNKNOWN_LAW_STEP) };
  const held = expectedForAnswer(j5, { verdict: "answer", result: { value: {} } });
  assert.equal(held.state, "refusal", "a law the corpus does not hold must be refused even when the API answers it");
  assert.equal(held.refusalCode, "identifier_unknown");
  assert.deepEqual(held.texts, [ABSENCE_NOTE]);
  const j2 = { step: NO_HIT_SEARCH_STEP, ...realMountExpectations(NO_HIT_SEARCH_STEP) };
  assert.deepEqual(expectedForAnswer(j2, answered).texts, [ABSENCE_NOTE], "the refinement sees the envelope the outcome came from");
  assert.equal(expectedForAnswer(j2, { verdict: "answer", result: { value: { hits: [{}] } } }).texts, undefined);
});

test("the real-mount runs give each step its expectations, then the API journeys against the same server, J8 bounded", async () => {
  const { mkdtemp, writeFile, rm } = await import("node:fs/promises");
  const { tmpdir } = await import("node:os");
  const { join } = await import("node:path");
  const mount = await mkdtemp(join(tmpdir(), "lex-real-mount-"));
  try {
    await writeFile(join(mount, "build-report.json"), JSON.stringify({ corpus: { Sha256: "a".repeat(64) }, luxembourgIndex: { Sha256: "b".repeat(64) } }));
    const asked = [];
    const startServer = async () => ({ origin: "http://127.0.0.1:1", close: async () => {} });
    const runs = await realMountRuns("api", mount, { servedByApi: true, keyboard: false, startServer }, "browser", "live", {
      runStep: async (apiOutput, givenMount, expected) => {
        asked.push(expected);
        return { observed: { answerState: "success" }, failures: [] };
      },
      apiRuns: async (apiOutput, givenMount, options) => {
        assert.equal(options.startServer, startServer, "the API journeys ask the same kind of server the pages do");
        assert.equal(options.eventsToVerify, REAL_MOUNT_EVENTS_VERIFIED);
        return [["J6, a question", []], ["J8, the event log", ["a failure"]]];
      },
    });
    assert.equal(asked.length, Object.keys(JOURNEY_STEPS).length + 3);
    assert.ok(asked.every((expected) => expected.fromApi === true && expected.servedByApi === true));
    assert.equal(asked.find((expected) => expected.step === JOURNEY_STEPS.coverage).corpusSha256, "a".repeat(64));
    for (const step of [PRE_HISTORY_READING_STEP, NO_HIT_SEARCH_STEP, UNKNOWN_LAW_STEP]) {
      assert.equal(typeof asked.find((expected) => expected.step === step)?.refine, "function", "each specification journey carries its refinement");
    }
    assert.deepEqual(runs.slice(-2).map(([label, { failures }]) => [label, failures]),
      [["J6, a question, with the real mount", []], ["J8, the event log, with the real mount", ["a failure"]]]);
    assert.equal(runs.at(-1)[1].observed.answerState, "api");
  } finally {
    await rm(mount, { recursive: true, force: true });
  }
});

/**
 * A stand-in API answering events, verify and answer_drift as a genesis log of `count` events does, counting verify
 * requests. It pages as the real `events` does: at most `limit` events after the cursor, `has_more`, and `next_after`
 * naming the last event served (the log's last when there is no more); a cursor past the last event is refused.
 * `overlap` makes the second page repeat the first page's last event, as a broken log would.
 */
async function eventLogServer(count, { limit = 200, overlap = false } = {}) {
  const { createServer } = await import("node:http");
  const logId = "e".repeat(64);
  const events = Array.from({ length: count }, (_, index) => ({ seq: index + 1, permalink: `/lu-legilux/w${index}/2024-02-01--${"f".repeat(64)}/fra` }));
  const page = (after) => {
    const rest = events.filter((event) => event.seq > (overlap && after > 0 ? after - 1 : after));
    const served = rest.slice(0, limit);
    const hasMore = rest.length > limit;
    return { events: served, has_more: hasMore, next_after: `${logId}:${hasMore ? served.at(-1).seq : count}` };
  };
  let verified = 0;
  const server = createServer((request, response) => {
    let text = "";
    request.on("data", (chunk) => { text += chunk; });
    request.on("end", () => {
      const { operation_id: operation, parameters = {} } = JSON.parse(text || "{}");
      let body;
      if (operation === "events" && parameters.after === `${"0".repeat(64)}:1`) body = { verdict: "refuse", refusal: { code: "snapshot_unknown" } };
      else if (operation === "events") {
        const after = parameters.after === undefined ? 0 : Number(parameters.after.split(":")[1]);
        if (after > count) {
          response.writeHead(400, { "content-type": "application/json" });
          response.end(JSON.stringify({ verdict: "refuse", refusal: { code: "request_schema_invalid" } }));
          return;
        }
        body = { verdict: "answer", result: { value: page(after) } };
      }
      else if (operation === "verify") {
        verified += 1;
        body = { verdict: "answer", result: { value: { verdict: "digest_matches" } } };
      } else if (operation === "answer_drift") body = { verdict: "answer", result: { value: { invalidated_answers: [], asserts_no_drift_in_law: false } } };
      else body = { verdict: "refuse", refusal: { code: "operation_unknown" } };
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify(body));
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  return {
    origin: `http://127.0.0.1:${server.address().port}`,
    verified: () => verified,
    close: () => new Promise((resolve) => server.close(resolve)),
  };
}

test("journey J8 on a real mount follows the log's own cursor, verifies a bounded number of permalinks, and an empty log cannot show the journey", async () => {
  // A population log answers a page at a time: the journey reads on from next_after and checks the next page starts
  // where the first ended.
  const population = await eventLogServer(65, { limit: 30 });
  try {
    assert.deepEqual(await eventsFailures(population.origin, { verifyAtMost: REAL_MOUNT_EVENTS_VERIFIED }), []);
    assert.equal(population.verified(), REAL_MOUNT_EVENTS_VERIFIED, "a population log holds thousands of events; the journey verifies the first few");
  } finally {
    await population.close();
  }
  const broken = await eventLogServer(65, { limit: 30, overlap: true });
  try {
    const failures = await eventsFailures(broken.origin, { verifyAtMost: 1 });
    assert.ok(failures.some((failure) => /does not start where the first ended/.test(failure)), failures.join("; "));
  } finally {
    await broken.close();
  }
  const fixture = await eventLogServer(5);
  try {
    assert.deepEqual(await eventsFailures(fixture.origin), []);
    assert.equal(fixture.verified(), 5, "the fixture mount's log is one page, verified whole, as before");
  } finally {
    await fixture.close();
  }
  const empty = await eventLogServer(0);
  try {
    const failures = await eventsFailures(empty.origin, { verifyAtMost: REAL_MOUNT_EVENTS_VERIFIED });
    assert.ok(failures.some((failure) => /holds no event to poll.*cannot show journey J8/.test(failure)), failures.join("; "));
  } finally {
    await empty.close();
  }
});

test("the API journeys run against the server the caller starts (the release image, a deployed revision) and close it", async () => {
  const server = await eventLogServer(3);
  let started = 0;
  let closed = 0;
  try {
    const results = await apiJourneyRuns(null, null, {
      startServer: async () => {
        started += 1;
        return { origin: server.origin, close: async () => { closed += 1; } };
      },
      eventsToVerify: 2,
    });
    assert.equal(started, 1);
    assert.equal(closed, 1);
    assert.deepEqual(results.map(([name]) => name.slice(0, 2)).filter((name, index, all) => all.indexOf(name) === index), ["J6", "J7", "J8"]);
    assert.deepEqual(results.at(-1)[1], [], "J8 against the stand-in log passes");
    assert.equal(server.verified(), 2, "and verified the two permalinks it was allowed");
  } finally {
    await server.close();
  }
});

test("the long act's journeys: the cited article on its second page, that page without scripts, and the next articles in place", () => {
  const permalink = `${DOSSIER_IDENTIFIER}/${READING_DATE}--${"a".repeat(64)}/fra`;
  const mount = { work_key: "loi-1991-08-10-n3", long_act: { date: READING_DATE, language: "fra", permalink, pages: 3, articles: 130, cited: "art_77" } };
  const [[, cited], [, second], [, inPlace]] = longActExpectations(mount);
  assert.equal(cited.step.path, `/en${permalink}/art_77`);
  assert.deepEqual(cited.step.pins, { identifier: DOSSIER_IDENTIFIER, date: READING_DATE, anchor: "art_77", language: "fra" });
  assert.deepEqual(cited.step.body.parameters, { identifier: permalink, date: READING_DATE, language: "fra", article: "art_77" }, "the page that holds the cited article");
  assert.ok(cited.texts.includes("Page 2 of 3: art_51 to art_100."));
  assert.equal(second.step.path, `/en${permalink}?page=2`, "the address the next-articles link names");
  assert.equal(second.step.rendered, true, "shown with no script run");
  assert.deepEqual(inPlace.step.then, { click: "[data-live-answer] a[data-next-page]", key: "Enter", until: "[data-pages-shown='1-2']", asks: 1 });

  // The next articles in place: one request, at page 2, and no navigation.
  const observed = {
    ...good(),
    text: "Pages 1 to 2 of 3: art_1er to art_100. 130 articles quoted",
    requests: [
      { url: `${ORIGIN}${inPlace.step.path}`, method: "GET", headers: {} },
      { url: `${ORIGIN}/api/v3/evidence_bundle`, method: "POST", headers: {}, headersSent: true, postData: JSON.stringify(inPlace.step.body) },
    ],
    location: `${ORIGIN}${inPlace.step.path}`,
    history: { atLoad: 2, atEnd: 2, state: "null" },
    cookie: "",
    then: "reached",
    serverRendered: true,
    withoutScripts: { answerState: "success" },
  };
  const expected = { origin: ORIGIN, ...inPlace, mustRender: true };
  assert.deepEqual(journeyVerdict(observed, expected), []);
  assert.ok(journeyVerdict({ ...observed, then: "clicked" }, expected).some((failure) => /never showed \[data-pages-shown='1-2'\]/.test(failure)), "the next articles never added");
  assert.ok(journeyVerdict({ ...observed, requests: observed.requests.slice(0, 1) }, expected).some((failure) => /not exactly one/.test(failure)), "the next page must be asked, once");
  assert.ok(journeyVerdict({ ...observed, location: `${ORIGIN}${second.step.path}` }, expected).some((failure) => /ended at/.test(failure)), "in place: no navigation");

  // A page after the first names itself canonical with its page, in the page's own language (the plan's item 3.6), and
  // nothing else after it.
  const page = { ...observed, then: undefined, text: "Page 2 of 3: art_51 to art_100. Previous articles 130 articles quoted", requests: observed.requests.slice(0, 1), location: `${ORIGIN}${second.step.path}`,
    canonical: `https://law.soufien.lu/en${permalink}?page=2`,
    alternates: [["fr", `https://law.soufien.lu/fr${permalink}?page=2`], ["en", `https://law.soufien.lu/en${permalink}?page=2`], ["x-default", `https://law.soufien.lu${permalink}?page=2`]] };
  const secondExpected = { origin: ORIGIN, ...second, mustRender: true };
  assert.deepEqual(journeyVerdict(page, secondExpected), []);
  assert.ok(journeyVerdict({ ...page, canonical: `https://law.soufien.lu/en${permalink}?page=2&x=1` }, secondExpected).some((failure) => /canonical/.test(failure)), "a canonical with another query fails");
  assert.ok(journeyVerdict({ ...page, canonical: `https://law.soufien.lu/en${permalink}` }, secondExpected).some((failure) => /canonical/.test(failure)), "page 2 named as page 1 fails");
});
