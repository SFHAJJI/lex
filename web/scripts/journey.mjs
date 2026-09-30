// One browser journey step against a live API: the Trust and Coverage page, read in a real browser.
//
// Two runs of one step. With a mount, the page must end in the coverage answer and show the digests
// of the corpus and index that mount holds; without one, it must end in the refusal card for
// `no_corpus_mounted`. In both, what the browser did is measured, not assumed: exactly one request to
// the API (`POST /api/v3/coverage`, no query string, no referrer), every other request a same-origin
// asset, nothing written to storage, nothing logged to the console, the page's CSP the reviewed one,
// and hydration clean.
//
// The API is the real `Lex.V3.Api`, run from a copy of its build output so the mount can sit beside
// it (`AppContext.BaseDirectory/v3-corpus`, which is where the API looks). The page is `dist-live/`,
// served with the API on one origin by `serve-live.mjs`. The browser is the one the evidence runs use,
// driven over the DevTools protocol by the same `Session`.
//
//   node scripts/journey.mjs --api <Lex.V3.Api build output> --mount <journey mount directory>
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

/**
 * What one run must show, as failures (empty means the run passed).
 *
 * @param {object} observed what the browser run read: `answerState`, `text`, `requests`
 *   (`{url, method, headers}`), `console`, `storage` (`{local, session}`), `csp`, `hydrated`
 * @param {object} expected `{origin, state: "success"|"refusal", corpusSha256?, indexSha256?, refusalCode?}`
 */
export function journeyVerdict(observed, expected) {
  const failures = [];
  if (observed.answerState !== expected.state) {
    failures.push(`the page ended in ${observed.answerState}, not ${expected.state}`);
  }
  if (expected.state === "success") {
    for (const digest of [expected.corpusSha256, expected.indexSha256]) {
      if (!observed.text.includes(digest)) failures.push(`the page does not show the mounted digest ${digest}`);
    }
  }
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
    if (url.pathname !== "/api/v3/coverage" || url.search !== "") failures.push(`an API request was not POST /api/v3/coverage: ${request.url}`);
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
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
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
  throw new Error(`the API did not answer within 60 s: ${stderr}`);
}

async function observe(browser, pageOrigin) {
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
        });
      } else if (message.method === "Network.requestWillBeSentExtraInfo") {
        // The headers the browser actually sent (cookies and the referrer included), where
        // requestWillBeSent reports only provisional ones.
        sentHeaders.set(message.params.requestId, message.params.headers);
      } else if (message.method === "Runtime.consoleAPICalled") {
        consoleMessages.push(message.params.args.map((arg) => arg.value ?? arg.description ?? "").join(" "));
      } else if (message.method === "Log.entryAdded") {
        consoleMessages.push(`${message.params.entry.level}: ${message.params.entry.text}`);
      }
    });
    for (const domain of ["Network", "Runtime", "Log", "Page"]) await session.send(`${domain}.enable`, {}, sessionId);
    await session.send("Page.navigate", { url: `${pageOrigin}/` }, sessionId);
    const evaluate = async (expression) =>
      (await session.send("Runtime.evaluate", { expression, returnByValue: true }, sessionId)).result.value;
    const deadline = Date.now() + ANSWER_DEADLINE_MS;
    let answerState = null;
    while (Date.now() < deadline) {
      answerState = await evaluate("document.querySelector('[data-answer-state]')?.dataset.answerState ?? null");
      if (answerState !== null && answerState !== "loading") break;
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
    const observed = await observe(browser, pageOrigin);
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
  const liveRoot = await buildLive();
  const browser = await findBrowser();
  const results = [
    ["with the fixture mount", await run(apiOutput, mount, { state: "success", corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 }, browser, liveRoot)],
    ["with no mount", await run(apiOutput, null, { state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
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
