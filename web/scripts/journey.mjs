// Browser journey steps against a live API, each read in a real browser.
//
// Seven steps, each run twice. Trust and Coverage: with a mount, the page must end in the coverage
// answer and show the digests of the corpus and index that mount holds. Search, dossier, reading,
// provision history, compare and radar: once the page has hydrated, the journey types into the form
// (a phrase; a work identifier; a work identifier and a date; a work identifier and an article id; a
// work identifier and two dates; two dates) and submits it;
// with a mount, the page must end in the answer, and the one request must carry exactly what was
// typed and nothing else. Without a mount, each must end in the refusal card for
// `no_corpus_mounted`. In every run, what the
// browser did is measured, not assumed: exactly one request to the API (`POST /api/v3/{operation}`,
// no query string, no referrer, no cookie), every other request a same-origin asset, the page still
// at its own address with no history entry added and no history state written, no cookie set,
// nothing written to storage, nothing logged to the console and no uncaught exception or unhandled
// rejection, the page's CSP the reviewed one, and hydration clean. (The history and cookie checks
// came from the review of #773: a page that pushed the phrase into the history and a cookie passed.)
//
// The API is the real `Lex.V3.Api`, run from a copy of its build output so the mount can sit beside
// it (`AppContext.BaseDirectory/v3-corpus`, which is where the API looks). The page is `dist-live/`,
// served with the API on one origin by `serve-live.mjs`. The browser is the one the evidence runs use,
// driven over the DevTools protocol by the same `Session`.
//
//   node scripts/journey.mjs --api <Lex.V3.Api build output> --mount <journey mount directory>
//                            [--live-root <a built live directory>] [--verbose]
//
// The mount is written by `V3JourneyMountTests` with `V3_WRITE_JOURNEY_MOUNT=<directory>`; it is the
// test fixture's mount, so the journey proves the wiring, not a real corpus.

import { spawn } from "node:child_process";
import { cp, mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { buildLive } from "./build-live.mjs";
import { createLiveServer } from "./serve-live.mjs";
import { Session, allocateDebuggerPort, findBrowser, waitForDebugger } from "./browser-evidence.mjs";
import { cspValue } from "./csp.mjs";

export const ANSWER_DEADLINE_MS = 30_000;

/** The phrase the search step types: on the fixture mount, 4 strict hits and 1 relaxed. */
export const SEARCH_PHRASE = "assemblée générale";

/** The identifier the dossier step types: the fixture mount's one work, asked in any held language. */
export const DOSSIER_IDENTIFIER = "/lu-legilux/loi-1991-08-10-n3";

/** The date the reading step types beside that identifier: the fixture state's own date. */
export const READING_DATE = "2024-02-01";

/** The article id the provision history step types beside that identifier. */
export const HISTORY_ANCHOR = "art_15";

/** The two steps: the page each loads, the operation it must ask, and what it does before waiting. */
export const JOURNEY_STEPS = Object.freeze({
  coverage: Object.freeze({ path: "/", operation: "coverage", body: null }),
  search: Object.freeze({
    path: "/search.html",
    operation: "search",
    typed: SEARCH_PHRASE,
    body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: SEARCH_PHRASE, language: "fra" }) }),
  }),
  dossier: Object.freeze({
    path: "/dossier.html",
    operation: "dossier",
    typed: DOSSIER_IDENTIFIER,
    body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER }) }),
  }),
  reading: Object.freeze({
    path: "/reading.html",
    operation: "evidence_bundle",
    typed: Object.freeze([DOSSIER_IDENTIFIER, READING_DATE]),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE }),
    }),
  }),
  compare: Object.freeze({
    path: "/compare.html",
    operation: "diff",
    typed: Object.freeze([DOSSIER_IDENTIFIER, READING_DATE, READING_DATE]),
    body: Object.freeze({
      operation_id: "diff",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: READING_DATE }),
    }),
  }),
  radar: Object.freeze({
    path: "/radar.html",
    operation: "changes_in_period",
    typed: Object.freeze([READING_DATE, READING_DATE]),
    body: Object.freeze({
      operation_id: "changes_in_period",
      parameters: Object.freeze({ date_from: READING_DATE, date_to: READING_DATE }),
    }),
  }),
  history: Object.freeze({
    path: "/history.html",
    operation: "article_history",
    typed: Object.freeze([DOSSIER_IDENTIFIER, HISTORY_ANCHOR]),
    body: Object.freeze({
      operation_id: "article_history",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, anchor: HISTORY_ANCHOR }),
    }),
  }),
});

/**
 * What one run must show, as failures (empty means the run passed).
 *
 * @param {object} observed what the browser run read: `answerState`, `text`, `requests`
 *   (`{url, method, headers, postData?}`), `console`, `storage` (`{local, session}`), `csp`,
 *   `hydrated`, `location` (where the page ended), `history` (`{atLoad, atEnd, state}`: the
 *   history length when the page had loaded and at the end, and the history state, as JSON) and
 *   `cookie` (`document.cookie` at the end)
 * @param {object} expected `{origin, state: "success"|"refusal", step?, corpusSha256?, indexSha256?,
 *   texts?, refusalCode?}`; `step` is a `JOURNEY_STEPS` entry and defaults to coverage
 */
export function journeyVerdict(observed, expected) {
  const failures = [];
  const step = expected.step ?? JOURNEY_STEPS.coverage;
  if (observed.answerState !== expected.state) {
    failures.push(`the page ended in ${observed.answerState}, not ${expected.state}`);
  }
  if (expected.state === "success" && step.operation === "coverage") {
    for (const digest of [expected.corpusSha256, expected.indexSha256]) {
      if (!observed.text.includes(digest)) failures.push(`the page does not show the mounted digest ${digest}`);
    }
  }
  for (const text of expected.texts ?? []) {
    if (!observed.text.includes(text)) failures.push(`the page does not show "${text}"`);
  }
  if (observed.location !== undefined && observed.location !== `${expected.origin}${step.path}`) {
    failures.push(`the page ended at ${observed.location}, not at ${expected.origin}${step.path}`);
  }
  if (observed.history !== undefined) {
    if (observed.history.atEnd !== observed.history.atLoad) {
      failures.push(`the page changed the history from ${observed.history.atLoad} entries to ${observed.history.atEnd}`);
    }
    if (observed.history.state !== "null") failures.push(`the page wrote history state: ${observed.history.state}`);
  }
  if (observed.cookie !== undefined && observed.cookie !== "") failures.push(`the page set a cookie: ${observed.cookie}`);
  if (expected.state === "refusal" && !observed.text.includes(expected.refusalCode)) {
    failures.push(`the page does not name the refusal ${expected.refusalCode}`);
  }

  const toApi = observed.requests.filter((request) => new URL(request.url).pathname.startsWith("/api/"));
  if (toApi.length !== 1) {
    failures.push(`the page made ${toApi.length} requests to the API, not exactly one`);
  }
  for (const request of toApi) {
    const url = new URL(request.url);
    if (url.origin !== expected.origin) failures.push(`an API request left the page's origin: ${request.url}`);
    if (url.pathname !== `/api/v3/${step.operation}` || url.search !== "") {
      failures.push(`an API request was not POST /api/v3/${step.operation}: ${request.url}`);
    }
    if (step.body !== null) {
      let body = null;
      try {
        body = JSON.parse(request.postData ?? "");
      } catch {
        // Recorded as not the body below.
      }
      if (JSON.stringify(body) !== JSON.stringify(step.body)) {
        failures.push(`the API request's body was ${request.postData ?? "not observed"}, not ${JSON.stringify(step.body)}`);
      }
    }
    if (request.method !== "POST") failures.push(`an API request used ${request.method}`);
    // The browser reports a provisional `Referer: ""` under the no-referrer policy: an empty value is
    // no referrer (observed in the first run of this journey), a non-empty one is a leak.
    const valueOf = (name) => Object.entries(request.headers ?? {})
      .find(([key]) => key.toLowerCase() === name)?.[1] ?? "";
    if (request.headersSent !== true) failures.push("the headers the API request was sent with were not observed");
    if (valueOf("referer") !== "") failures.push("the API request carried a referrer");
    if (valueOf("cookie") !== "") failures.push("the API request carried a cookie");
  }
  for (const request of observed.requests) {
    const url = new URL(request.url);
    if (url.protocol === "data:") continue;
    if (url.origin !== expected.origin) failures.push(`the page reached another origin: ${request.url}`);
    if (!url.pathname.startsWith("/api/") && request.method !== "GET") {
      failures.push(`the page sent ${request.method} for an asset: ${request.url}`);
    }
  }
  if (observed.console.length > 0) failures.push(`the page logged ${observed.console.length} console message(s): ${observed.console.join(" | ")}`);
  if (observed.storage.local !== 0 || observed.storage.session !== 0) failures.push("the page wrote to storage");
  if (observed.csp !== cspValue()) failures.push("the page's CSP is not the reviewed one");
  if (observed.hydrated !== "clean") failures.push(`hydration was ${observed.hydrated ?? "never reported"}, not clean`);
  return failures;
}

function listen(server) {
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
}

async function freePort() {
  const { createServer } = await import("node:net");
  const probe = createServer();
  await new Promise((resolve) => probe.listen(0, "127.0.0.1", resolve));
  const { port } = probe.address();
  await new Promise((resolve) => probe.close(resolve));
  return port;
}

async function startApi(apiOutput, mount) {
  const home = await mkdtemp(join(tmpdir(), "lex-journey-api-"));
  await cp(apiOutput, home, { recursive: true });
  await rm(join(home, "v3-corpus"), { recursive: true, force: true });
  if (mount) await cp(mount, join(home, "v3-corpus"), { recursive: true });
  const port = await freePort();
  const origin = `http://127.0.0.1:${port}`;
  const child = spawn("dotnet", [join(home, "Lex.V3.Api.dll")], {
    cwd: home,
    env: { ...process.env, ASPNETCORE_URLS: origin, DOTNET_NOLOGO: "1" },
    stdio: ["ignore", "ignore", "pipe"],
  });
  let stderr = "";
  child.stderr.on("data", (chunk) => { stderr += chunk; });
  let exited = false;
  child.on("exit", () => { exited = true; });
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline && !exited) {
    try {
      const answer = await fetch(`${origin}/api/v3/coverage`, {
        method: "POST", headers: { "content-type": "application/json" }, body: '{"operation_id":"coverage","parameters":{}}',
      });
      if (answer.status === 200) return { origin, child, home, stderr: () => stderr };
    } catch {
      // Not listening yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  child.kill();
  await new Promise((resolve) => setTimeout(resolve, 500));
  await rm(home, { recursive: true, force: true }).catch(() => {});
  throw new Error(exited
    ? `the API exited before it answered: ${stderr}`
    : `the API did not answer within 60 s: ${stderr}`);
}

/**
 * A form step's action: once hydrated, type into the form's text fields in order (one text, or one per
 * field) and submit it.
 */
async function typeAndSubmit(session, sessionId, evaluate, deadline, typed) {
  while (Date.now() < deadline && (await evaluate("document.documentElement.dataset.hydrated ?? null")) === null) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  const texts = Array.isArray(typed) ? typed : [typed];
  for (const [index, text] of texts.entries()) {
    await evaluate(`document.querySelectorAll('form[role=search] input')[${index}].focus()`);
    await session.send("Input.insertText", { text }, sessionId);
  }
  await evaluate("document.querySelector('form[role=search] button[type=submit]').click()");
}

async function observe(browser, pageOrigin, step) {
  const port = allocateDebuggerPort(9800, 300);
  const profile = await mkdtemp(join(tmpdir(), "lex-journey-cdp-"));
  const chrome = spawn(browser, [
    "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check", "about:blank",
  ], { stdio: "ignore" });
  try {
    const session = await Session.open(await waitForDebugger(port));
    const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });
    const requests = [];
    const sentHeaders = new Map();
    const consoleMessages = [];
    session.on((message) => {
      if (message.sessionId !== sessionId) return;
      if (message.method === "Network.requestWillBeSent") {
        requests.push({
          id: message.params.requestId,
          url: message.params.request.url,
          method: message.params.request.method,
          headers: message.params.request.headers,
          postData: message.params.request.postData,
        });
      } else if (message.method === "Network.requestWillBeSentExtraInfo") {
        // The headers the browser actually sent (cookies and the referrer included), where
        // requestWillBeSent reports only provisional ones.
        sentHeaders.set(message.params.requestId, message.params.headers);
      } else if (message.method === "Runtime.consoleAPICalled") {
        consoleMessages.push(message.params.args.map((arg) => arg.value ?? arg.description ?? "").join(" "));
      } else if (message.method === "Log.entryAdded") {
        consoleMessages.push(`${message.params.entry.level}: ${message.params.entry.text}`);
      } else if (message.method === "Runtime.exceptionThrown") {
        // An uncaught error or unhandled rejection reaches neither the console API nor the log
        // (review of #766: a throwing page passed), so it is observed on its own.
        const details = message.params.exceptionDetails;
        consoleMessages.push(`exception: ${details.exception?.description ?? details.text}`);
      }
    });
    for (const domain of ["Network", "Runtime", "Log", "Page"]) await session.send(`${domain}.enable`, {}, sessionId);
    await session.send("Page.navigate", { url: `${pageOrigin}${step.path}` }, sessionId);
    const evaluate = async (expression) =>
      (await session.send("Runtime.evaluate", { expression, returnByValue: true }, sessionId)).result.value;
    const deadline = Date.now() + ANSWER_DEADLINE_MS;
    while (Date.now() < deadline && (await evaluate("document.readyState")) !== "complete") {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    const historyAtLoad = await evaluate("history.length");
    if (step.typed !== undefined) await typeAndSubmit(session, sessionId, evaluate, deadline, step.typed);
    let answerState = null;
    while (Date.now() < deadline) {
      answerState = await evaluate("document.querySelector('[data-answer-state]')?.dataset.answerState ?? null");
      if (answerState !== null && answerState !== "loading" && answerState !== "idle") break;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    // Let a late request or log line arrive before the observation is closed.
    await new Promise((resolve) => setTimeout(resolve, 500));
    return {
      answerState,
      text: await evaluate("document.body.innerText"),
      requests: requests.map(({ id, ...request }) => ({
        ...request,
        headers: sentHeaders.get(id) ?? request.headers,
        headersSent: sentHeaders.has(id),
      })),
      console: consoleMessages,
      storage: await evaluate("({ local: localStorage.length, session: sessionStorage.length })"),
      csp: await evaluate("document.querySelector('meta[http-equiv=\"Content-Security-Policy\"]')?.content ?? null"),
      hydrated: await evaluate("document.documentElement.dataset.hydrated ?? null"),
      location: await evaluate("location.href"),
      history: {
        atLoad: historyAtLoad,
        atEnd: await evaluate("history.length"),
        state: await evaluate("JSON.stringify(history.state)"),
      },
      cookie: await evaluate("document.cookie"),
    };
  } finally {
    chrome.kill();
    await new Promise((resolve) => setTimeout(resolve, 500));
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
}

async function run(apiOutput, mount, expected, browser, liveRoot) {
  const api = await startApi(apiOutput, mount);
  const live = createLiveServer({ root: liveRoot, apiOrigin: api.origin });
  const pageOrigin = await listen(live);
  try {
    const observed = await observe(browser, pageOrigin, expected.step);
    return { observed, failures: journeyVerdict(observed, { ...expected, origin: pageOrigin }) };
  } finally {
    await new Promise((resolve) => live.close(resolve));
    api.child.kill();
    await new Promise((resolve) => setTimeout(resolve, 500));
    await rm(api.home, { recursive: true, force: true }).catch(() => {});
  }
}

async function main(argv) {
  const argument = (name) => {
    const index = argv.indexOf(name);
    if (index < 0 || index + 1 >= argv.length) throw new Error(`usage: node scripts/journey.mjs --api <dir> --mount <dir> (missing ${name})`);
    return argv[index + 1];
  };
  const apiOutput = argument("--api");
  const mount = argument("--mount");
  if (!(await readdir(apiOutput)).includes("Lex.V3.Api.dll")) throw new Error(`${apiOutput} holds no Lex.V3.Api.dll`);
  const journeyMount = JSON.parse(await readFile(join(mount, "journey-mount.json"), "utf8"));
  // `--live-root` serves a directory built elsewhere instead of building one: how a deliberately
  // broken page is shown to fail the journey.
  const liveRoot = argv.includes("--live-root") ? argument("--live-root") : await buildLive();
  const browser = await findBrowser();
  const { coverage, search, dossier, reading, history, compare, radar } = JOURNEY_STEPS;
  const results = [
    ["coverage, with the fixture mount", await run(apiOutput, mount, { step: coverage, state: "success", corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 }, browser, liveRoot)],
    ["coverage, with no mount", await run(apiOutput, null, { step: coverage, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["search, with the fixture mount", await run(apiOutput, mount, { step: search, state: "success", texts: [`“${SEARCH_PHRASE}” in fra: 4 with the exact phrase, 1 with every word, in 1 work.`, "art_15 in", "The first hits in the stated order, not the best hits."] }, browser, liveRoot)],
    ["search, with no mount", await run(apiOutput, null, { step: search, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["dossier, with the fixture mount", await run(apiOutput, mount, { step: dossier, state: "success", texts: [journeyMount.work_key, "1 state, from 2024-02-01 to 2024-02-01.", "What this dossier does not hold"] }, browser, liveRoot)],
    ["dossier, with no mount", await run(apiOutput, null, { step: dossier, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["reading, with the fixture mount", await run(apiOutput, mount, { step: reading, state: "success", texts: ["the state applying from 2024-02-01", "49 articles quoted", "Art. 15.", "Text served under agreed_same_run_cc_by"] }, browser, liveRoot)],
    ["reading, with no mount", await run(apiOutput, null, { step: reading, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["history, with the fixture mount", await run(apiOutput, mount, { step: history, state: "success", texts: [`${HISTORY_ANCHOR} in loi-1991-08-10-n3`, "Carried by 1 held state, from 2024-02-01", "first held wording"] }, browser, liveRoot)],
    ["history, with no mount", await run(apiOutput, null, { step: history, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["compare, with the fixture mount", await run(apiOutput, mount, { step: compare, state: "success", texts: [`loi-1991-08-10-n3: ${READING_DATE} against ${READING_DATE}.`, "The same version applied on both dates."] }, browser, liveRoot)],
    ["compare, with no mount", await run(apiOutput, null, { step: compare, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["radar, with the fixture mount", await run(apiOutput, mount, { step: radar, state: "success", texts: [`${READING_DATE} to ${READING_DATE}: 1 state of 1 work, of 1 held.`, "not compared: the first state this index holds"] }, browser, liveRoot)],
    ["radar, with no mount", await run(apiOutput, null, { step: radar, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
  ];
  let failed = false;
  for (const [label, { observed, failures }] of results) {
    const toApi = observed.requests.filter((request) => new URL(request.url).pathname.startsWith("/api/")).length;
    console.log(`${label}: ${observed.answerState}; ${observed.requests.length} requests (${toApi} to the API); ` +
      `console ${observed.console.length}; hydration ${observed.hydrated}; ${failures.length === 0 ? "PASS" : "FAIL"}`);
    for (const failure of failures) console.log(`  - ${failure}`);
    if (argv.includes("--verbose")) {
      for (const request of observed.requests) {
        console.log(`  ${request.method} ${request.url} (${request.headersSent ? "sent" : "provisional"} headers) ${JSON.stringify(request.headers ?? {})}`);
      }
    }
    failed ||= failures.length > 0;
  }
  return failed ? 1 : 0;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2)).then((code) => process.exit(code), (error) => {
    console.error(error.message);
    process.exit(2);
  });
}
