// Browser journey steps against a live API, each read in a real browser.
//
// Eight steps, each run twice. Trust and Coverage: with a mount, the page must end in the coverage
// answer and show the digests of the corpus and index that mount holds. Search, dossier, reading,
// provision history, compare, radar and the export composer: once the page has hydrated, the journey
// types into the form (a phrase; a work identifier; a work identifier and a date; a work identifier
// and an article id; a work identifier and two dates; two dates; a work identifier and a date) and
// submits it; the export composer then pins the first article and must show the composed export,
// with no second request;
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
//                            [--live-root <a built live directory>] [--served-by-api] [--keyboard] [--verbose]
//
// With `--keyboard` every form step is driven by the keyboard alone (the launch contract's keyboard
// path): from the top of the page, Tab until each text field has focus, type, Enter to submit; the
// export composer's pin is reached by Tab and checked with Space. Every focus stop must show a focus
// indicator. In every run, keyboard or not, the answer must be written into a polite live region the
// server already rendered (the screen-reader path).
//
// With `--served-by-api` the pages are not served by `serve-live.mjs`: the built directory is placed
// beside the API as `v3-web` and the API serves it on its own origin (Decision 95, ruling 3), and
// every run also checks the headers the page arrived with: the page's reviewed CSP plus
// `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer` and `nosniff`.
//
// Every run also holds the API process to the launch contract's "no query text, IP or user agent
// recorded": its standard output and error are captured from start to end, and its directory is
// watched from when it first answers until the page has asked, with every file listed at both ends.
// What it writes before it first answers is its startup, bounded in source by
// `PublicRequestRecordingTests` (three startup messages, logging providers cleared) and held here to
// carry none of the run's text. From then on the run fails if the process wrote anything at all
// (naming the typed text, the browser's user agent or the loopback address if it wrote those), or if
// any file under its directory was touched, including one written and deleted before the run ended.
//
// Every run also holds what the page cites to the launch contract's first promise, "`verify` resolves
// every citation the product emitted in the journey suite": each permalink the answer prints must be
// hash-pinned, a step that cites must print one on an answer, and once the run is recorded each is
// asked of the API's `verify`, which must find the digest it pins (`digest_matches`), that very
// state, and the article it names.
//
// The mount is written by `V3JourneyMountTests` with `V3_WRITE_JOURNEY_MOUNT=<directory>`; it is the
// test fixture's mount, so the journey proves the wiring, not a real corpus.

import { spawn } from "node:child_process";
import { watch } from "node:fs";
import { cp, mkdtemp, readFile, readdir, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { buildLive } from "./build-live.mjs";
import { createLiveServer } from "./serve-live.mjs";
import { Session, allocateDebuggerPort, findBrowser, waitForDebugger } from "./browser-evidence.mjs";
import { cspValue } from "./csp.mjs";
import { EXPORT_WATERMARK } from "./export-build.mjs";

export const ANSWER_DEADLINE_MS = 30_000;

/** The phrase the search step types: on the fixture mount, 4 strict hits and 1 relaxed. */
export const SEARCH_PHRASE = "assemblée générale";

/** The identifier the dossier step types: the fixture mount's one work, asked in any held language. */
export const DOSSIER_IDENTIFIER = "/lu-legilux/loi-1991-08-10-n3";

/** The date the reading step types beside that identifier: the fixture state's own date. */
export const READING_DATE = "2024-02-01";

/** The article id the provision history step types beside that identifier. */
export const HISTORY_ANCHOR = "art_15";

/**
 * A hash-pinned Luxembourg permalink, as `verify` takes it: the stable coordinate, `--` and the state
 * digest, and an article id after `#` (the grammar `V3CitationVerificationTests` holds the served
 * answers to).
 */
export const PINNED_PERMALINK = /^\/lu-legilux\/[a-z0-9_-]+\/\d{4}-\d{2}-\d{2}--([0-9a-f]{64})(?:#([^#\s]+))?$/;

/**
 * The steps: the page each loads, the operation it must ask, and what it does before waiting. A step
 * that `cites` prints at least one citation on an answer.
 */
export const JOURNEY_STEPS = Object.freeze({
  coverage: Object.freeze({ path: "/", operation: "coverage", body: null }),
  search: Object.freeze({
    path: "/search.html",
    cites: true,
    operation: "search",
    typed: SEARCH_PHRASE,
    body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: SEARCH_PHRASE, language: "fra" }) }),
  }),
  dossier: Object.freeze({
    path: "/dossier.html",
    cites: true,
    operation: "dossier",
    typed: DOSSIER_IDENTIFIER,
    body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER }) }),
  }),
  reading: Object.freeze({
    path: "/reading.html",
    cites: true,
    operation: "evidence_bundle",
    typed: Object.freeze([DOSSIER_IDENTIFIER, READING_DATE]),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE }),
    }),
  }),
  compare: Object.freeze({
    path: "/compare.html",
    cites: true,
    operation: "diff",
    typed: Object.freeze([DOSSIER_IDENTIFIER, READING_DATE, READING_DATE]),
    body: Object.freeze({
      operation_id: "diff",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date_from: READING_DATE, date_to: READING_DATE }),
    }),
  }),
  radar: Object.freeze({
    path: "/radar.html",
    cites: true,
    operation: "changes_in_period",
    typed: Object.freeze([READING_DATE, READING_DATE]),
    body: Object.freeze({
      operation_id: "changes_in_period",
      parameters: Object.freeze({ date_from: READING_DATE, date_to: READING_DATE }),
    }),
  }),
  export: Object.freeze({
    path: "/export.html",
    cites: true,
    operation: "evidence_bundle",
    typed: Object.freeze([DOSSIER_IDENTIFIER, READING_DATE]),
    // Once the reading has answered: pin the first article, and the export must be composed.
    then: Object.freeze({ click: "input[data-pin]", until: "[data-export-state=composed]" }),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE }),
    }),
  }),
  history: Object.freeze({
    path: "/history.html",
    cites: true,
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
  if (expected.state === "success" && step.then !== undefined && observed.then !== "reached") {
    failures.push(`the page never showed ${step.then.until} once ${step.then.click} was clicked (${observed.then ?? "not clicked"})`);
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
  // The answer is written into a polite live region that exists before it arrives, so a screen reader
  // hears it (the launch contract's screen-reader path).
  if (observed.liveRegion !== undefined) {
    if (observed.liveRegion.atLoad !== "polite") failures.push(`the answer's region at load is ${observed.liveRegion.atLoad === null ? "in no live region" : `aria-live=${observed.liveRegion.atLoad}`}, not polite`);
    if (observed.liveRegion.atEnd !== "polite") failures.push(`the answer arrived ${observed.liveRegion.atEnd === null ? "outside any live region" : `in aria-live=${observed.liveRegion.atEnd}`}, not a polite one`);
  }
  // The keyboard path: every field reached by Tab alone, every stop showing where focus is.
  if (expected.keyboard && step.typed !== undefined) {
    const keys = observed.keyboard;
    if (keys === undefined) failures.push("the run took no keyboard path");
    else {
      if (keys.placed !== keys.wanted) failures.push(`Tab reached ${keys.placed} of the form's ${keys.wanted} text fields`);
      // Not `<`: a count the page never kept (undefined) must fail, not pass.
      if (!(keys.keyPresses >= keys.characters)) failures.push(`text arrived without key presses: ${keys.characters} characters typed, ${keys.keyPresses} character keys pressed`);
      for (const stop of keys.stops.filter((candidate) => !candidate.ring)) {
        failures.push(`a focus stop shows no focus indicator: ${stop.tag}${stop.type ? `[type=${stop.type}]` : ""} "${stop.label}"`);
      }
    }
  }
  if (observed.citations !== undefined) {
    // The launch contract's first promise, on what the journey suite emitted: every citation a page
    // prints is hash-pinned, and `verify` finds the very state and article it pins.
    const pinned = observed.citations.filter((citation) => PINNED_PERMALINK.test(citation));
    for (const citation of observed.citations.filter((candidate) => !PINNED_PERMALINK.test(candidate))) {
      failures.push(`the page printed ${citation}, which is not a hash-pinned permalink`);
    }
    if (step.cites && expected.state === "success" && observed.citations.length === 0) failures.push("the page printed no citation");
    if (observed.verifications !== undefined) {
      const verified = new Set(observed.verifications.map((check) => check.identifier));
      for (const citation of pinned.filter((candidate) => !verified.has(candidate))) failures.push(`${citation} was not verified`);
      for (const check of observed.verifications) {
        const [, digest, anchor = null] = check.identifier.match(PINNED_PERMALINK) ?? [];
        if (check.refusal !== null) failures.push(`verify refused ${check.identifier} with ${check.refusal}`);
        else if (check.verdict !== "digest_matches") failures.push(`verify found ${check.identifier} ${check.verdict}, not digest_matches`);
        else if (check.stateSha256 !== digest) failures.push(`verify of ${check.identifier} named the state ${check.stateSha256}`);
        else if (anchor !== null && check.requestedAnchor !== anchor) failures.push(`verify of ${check.identifier} named the article ${check.requestedAnchor}`);
      }
    }
  }
  if (observed.paint !== undefined && observed.paint.unnamed.length > 0) {
    failures.push(`meaning by colour alone: ${[...new Set(observed.paint.unnamed)].join(", ")} painted with no words or accessible name`);
  }
  if (observed.quotes !== undefined) {
    // Every quotation carries, beside it, what the launch contract's first promise names: its text and
    // body digests, its official source, and a permalink that pins its very article (review of #805:
    // the export composer quoted 49 articles and cited only their state).
    for (const quote of observed.quotes) {
      if (!quote.codes.some((code) => code.match(PINNED_PERMALINK)?.[2] === quote.article)) {
        failures.push(`the quote of ${quote.article} carries no citation that pins it`);
      }
      const digests = quote.codes.filter((code) => /^[0-9a-f]{64}$/.test(code)).length;
      if (digests < 2 || !quote.codes.some((code) => /^https?:\/\//.test(code))) {
        failures.push(`the quote of ${quote.article} does not show its text digest, body digest and official source`);
      }
    }
  }
  if (observed.api !== undefined) {
    // What the API process recorded while serving the run: nothing, on its outputs or in its files.
    const typed = step.typed === undefined ? [] : [step.typed].flat();
    const everything = `${observed.api.startup ?? ""}${observed.api.output}`;
    for (const text of typed) {
      if (everything.includes(text)) failures.push(`the API process recorded the query text "${text}"`);
    }
    if (observed.userAgent && everything.includes(observed.userAgent)) failures.push("the API process recorded the browser's user agent");
    if (observed.api.output.includes("127.0.0.1")) failures.push("the API process recorded an address");
    if (observed.api.output.trim() !== "") failures.push(`the API process wrote output during the run: ${observed.api.output.trim().slice(0, 200)}`);
    if (observed.api.changedFiles.length > 0) failures.push(`the API process wrote files: ${observed.api.changedFiles.join(", ")}`);
    const touched = [...new Set((observed.api.fileEvents ?? []).map(([, path]) => path))];
    if (touched.length > 0) failures.push(`the API process touched files while serving the run: ${touched.join(", ")}`);
  }
  if (expected.servedByApi) {
    const header = (name) => Object.entries(observed.pageHeaders ?? {}).find(([key]) => key.toLowerCase() === name)?.[1] ?? null;
    const policy = `${cspValue()}; frame-ancestors 'none'`;
    if (header("content-security-policy") !== policy) failures.push(`the page arrived with the CSP ${header("content-security-policy")}, not ${policy}`);
    if (!/^max-age=\d+/.test(header("strict-transport-security") ?? "")) failures.push("the page arrived without HSTS");
    if (header("referrer-policy") !== "no-referrer") failures.push(`the page arrived with Referrer-Policy ${header("referrer-policy")}`);
    if (header("x-content-type-options") !== "nosniff") failures.push("the page arrived without nosniff");
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

/**
 * Every change the file system reports under a directory from now until `stop()`, as `[event, path]`.
 * Two listings, one before and one after, cannot see a file written and deleted between them (review
 * of #801); a watch held for the whole interval does.
 */
export function watchFiles(root) {
  const events = [];
  const watcher = watch(root, { recursive: true }, (event, name) => { events.push([event, String(name ?? "")]); });
  return {
    async stop() {
      // The system reports a change after it happens; a short wait lets the last ones arrive.
      await new Promise((resolve) => setTimeout(resolve, 250));
      watcher.close();
      return events;
    },
  };
}

/** Every file under a directory, as its relative path and its size and modification time. */
async function listFiles(root) {
  const files = new Map();
  for (const entry of await readdir(root, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const path = join(entry.parentPath ?? entry.path, entry.name);
    const facts = await stat(path);
    files.set(path.slice(root.length + 1), `${facts.size}:${facts.mtimeMs}`);
  }
  return files;
}

async function startApi(apiOutput, mount, webRoot = null) {
  const home = await mkdtemp(join(tmpdir(), "lex-journey-api-"));
  await cp(apiOutput, home, { recursive: true });
  await rm(join(home, "v3-corpus"), { recursive: true, force: true });
  await rm(join(home, "v3-web"), { recursive: true, force: true });
  if (mount) await cp(mount, join(home, "v3-corpus"), { recursive: true });
  if (webRoot) await cp(webRoot, join(home, "v3-web"), { recursive: true });
  const port = await freePort();
  const origin = `http://127.0.0.1:${port}`;
  const child = spawn("dotnet", [join(home, "Lex.V3.Api.dll")], {
    cwd: home,
    env: { ...process.env, ASPNETCORE_URLS: origin, DOTNET_NOLOGO: "1" },
    stdio: ["ignore", "pipe", "pipe"],
  });
  // Both outputs, from the first byte: what the process records is measured, not assumed.
  let stderr = "";
  let output = "";
  child.stdout.on("data", (chunk) => { output += chunk; });
  child.stderr.on("data", (chunk) => { stderr += chunk; output += chunk; });
  let exited = false;
  child.on("exit", () => { exited = true; });
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline && !exited) {
    try {
      const answer = await fetch(`${origin}/api/v3/coverage`, {
        method: "POST", headers: { "content-type": "application/json" }, body: '{"operation_id":"coverage","parameters":{}}',
      });
      if (answer.status === 200) {
        return { origin, child, home, stderr: () => stderr, output: () => output, outputAtStart: output.length, filesAtStart: await listFiles(home), fileWatch: watchFiles(home) };
      }
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

const KEYS = Object.freeze({ Tab: { code: "Tab", vk: 9 }, Enter: { code: "Enter", vk: 13, text: String.fromCharCode(13) }, " ": { code: "Space", vk: 32, text: " " } });

/** One key pressed as a keyboard presses it: down, the character it types (if any), up. */
async function pressKey(session, sessionId, key) {
  const { code, vk, text } = KEYS[key];
  await session.send("Input.dispatchKeyEvent", { type: "rawKeyDown", key, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk }, sessionId);
  if (text !== undefined) await session.send("Input.dispatchKeyEvent", { type: "char", key, code, text, windowsVirtualKeyCode: vk }, sessionId);
  await session.send("Input.dispatchKeyEvent", { type: "keyUp", key, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk }, sessionId);
}

/**
 * Text typed as a keyboard types it: each character its own key press (down, with the character, then
 * up), so the page receives keydown, keypress and input for every character. `Input.insertText`
 * changes a field with no key event at all, so a keyboard path built on it proves nothing about typing
 * (review of #802).
 */
async function typeByKeys(session, sessionId, text) {
  for (const character of text) {
    await session.send("Input.dispatchKeyEvent", { type: "keyDown", key: character, text: character, unmodifiedText: character }, sessionId);
    await session.send("Input.dispatchKeyEvent", { type: "keyUp", key: character }, sessionId);
  }
}

/** Counts, in the page, the character keys pressed (a key whose name is one character). */
const COUNT_CHARACTER_KEYS = `(() => {
  window.__journeyCharacterKeys = 0;
  document.addEventListener('keydown', (event) => { if ([...event.key].length === 1) window.__journeyCharacterKeys += 1; }, true);
})()`;

/** What has focus, and whether it shows a focus indicator (an outline or a shadow). */
const FOCUSED = `(() => {
  const el = document.activeElement;
  if (!el || el === document.body) return null;
  const style = getComputedStyle(el);
  const ring = (parseFloat(style.outlineWidth) > 0 && style.outlineStyle !== 'none') || (style.boxShadow !== 'none' && style.boxShadow !== '');
  return { tag: el.tagName.toLowerCase(), type: el.getAttribute('type'), inForm: el.closest('form[role=search]') !== null, pin: el.matches('input[data-pin]'), label: (el.labels?.[0]?.textContent ?? el.textContent ?? '').trim().slice(0, 40), ring };
})()`;

/**
 * The keyboard path through a form step: from the top of the page, Tab until each of the form's text
 * fields (text or search) has focus in turn, type into it key by key, then press Enter, which submits
 * the form as a keyboard user submits it. Every focus stop on the way is recorded with whether it
 * shows a focus indicator, and the page counts the character keys it received.
 */
async function keyboardTypeAndSubmit(session, sessionId, evaluate, deadline, typed) {
  while (Date.now() < deadline && (await evaluate("document.documentElement.dataset.hydrated ?? null")) === null) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  await evaluate("document.activeElement && document.activeElement.blur(); window.scrollTo(0, 0)");
  await evaluate(COUNT_CHARACTER_KEYS);
  const texts = Array.isArray(typed) ? typed : [typed];
  const stops = [];
  let placed = 0;
  let characters = 0;
  for (let tab = 0; tab < 40 && placed < texts.length; tab += 1) {
    await pressKey(session, sessionId, "Tab");
    const focused = await evaluate(FOCUSED);
    if (focused === null) continue;
    stops.push(focused);
    if (focused.inForm && focused.tag === "input" && (focused.type === "text" || focused.type === "search")) {
      await typeByKeys(session, sessionId, texts[placed]);
      characters += [...texts[placed]].length;
      placed += 1;
    }
  }
  const keyPresses = await evaluate("window.__journeyCharacterKeys");
  if (placed === texts.length) await pressKey(session, sessionId, "Enter");
  return { stops, placed, wanted: texts.length, characters, keyPresses };
}

/** The keyboard path to a pin: Tab until a pin has focus, then Space, which checks it. */
async function keyboardPin(session, sessionId, evaluate, stops) {
  for (let tab = 0; tab < 80; tab += 1) {
    await pressKey(session, sessionId, "Tab");
    const focused = await evaluate(FOCUSED);
    if (focused === null) continue;
    stops.push(focused);
    if (focused.pin) {
      await pressKey(session, sessionId, " ");
      return "clicked";
    }
  }
  return "absent";
}

/**
 * Every element whose meaning could lie in paint alone: painted apart from what is behind it (a
 * background image, or a background colour other than the one it sits on) and saying nothing in words
 * or in an accessible name. The launch contract's "no meaning by colour alone", measured on the page as
 * the browser paints it; decorative elements declared `aria-hidden` are exempt.
 */
const PAINT_ONLY = `(() => {
  const transparent = (colour) => colour === 'transparent' || colour === 'rgba(0, 0, 0, 0)';
  const behind = (el) => {
    for (let node = el; node !== null; node = node.parentElement) {
      const colour = getComputedStyle(node).backgroundColor;
      if (!transparent(colour)) return colour;
    }
    return 'rgb(255, 255, 255)';
  };
  const found = [];
  let painted = 0;
  for (const el of document.querySelectorAll('body *')) {
    if (el.closest('[aria-hidden="true"]') !== null || el.offsetParent === null) continue;
    const style = getComputedStyle(el);
    const paints = style.backgroundImage !== 'none'
      || (!transparent(style.backgroundColor) && style.backgroundColor !== behind(el.parentElement));
    if (!paints) continue;
    painted += 1;
    const named = el.textContent.trim() !== '' || (el.getAttribute('aria-label') ?? '').trim() !== ''
      || (el.getAttribute('alt') ?? '').trim() !== '' || (el.labels?.length ?? 0) > 0;
    if (!named) found.push(el.tagName.toLowerCase() + (el.className ? '.' + String(el.className).split(' ')[0] : ''));
  }
  return { painted, unnamed: found };
})()`;

/** The politeness of the live region the answer is written into, or null when it is in none. */
const ANSWER_LIVE_REGION = "document.querySelector('[data-answer-state]')?.closest('[aria-live]')?.getAttribute('aria-live') ?? null";

async function observe(browser, pageOrigin, step, { keyboard = false } = {}) {
  const port = allocateDebuggerPort(9800, 300);
  const profile = await mkdtemp(join(tmpdir(), "lex-journey-cdp-"));
  const chrome = spawn(browser, [
    "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check",
    // No component or background downloads: a fresh profile per run otherwise leaves Chrome's component
    // packages in the temporary directory, about 12 MB each, never removed (1,581 of them, 4.4 GB, by
    // 2026-09-30).
    "--disable-component-update", "--disable-background-networking",
    "about:blank",
  ], { stdio: "ignore" });
  try {
    const session = await Session.open(await waitForDebugger(port));
    const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });
    const requests = [];
    const sentHeaders = new Map();
    const consoleMessages = [];
    let documentId = null;
    let pageHeaders = null;
    session.on((message) => {
      if (message.sessionId !== sessionId) return;
      if (message.method === "Network.requestWillBeSent") {
        if (documentId === null && message.params.type === "Document") documentId = message.params.requestId;
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
      } else if (message.method === "Network.responseReceivedExtraInfo" && message.params.requestId === documentId) {
        // The headers the page's own response carried, as the browser received them.
        pageHeaders = message.params.headers;
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
    const liveRegionAtLoad = await evaluate(ANSWER_LIVE_REGION);
    let keys;
    if (step.typed !== undefined && keyboard) keys = await keyboardTypeAndSubmit(session, sessionId, evaluate, deadline, step.typed);
    else if (step.typed !== undefined) await typeAndSubmit(session, sessionId, evaluate, deadline, step.typed);
    let answerState = null;
    while (Date.now() < deadline) {
      answerState = await evaluate("document.querySelector('[data-answer-state]')?.dataset.answerState ?? null");
      if (answerState !== null && answerState !== "loading" && answerState !== "idle") break;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    // A step that acts on the answer (the export composer's pin) acts only on a success, and must
    // reach its state before the deadline.
    let then;
    if (step.then !== undefined && answerState === "success") {
      then = keyboard
        ? await keyboardPin(session, sessionId, evaluate, keys.stops)
        : await evaluate(`(() => { const node = document.querySelector(${JSON.stringify(step.then.click)}); if (node === null) return "absent"; node.click(); return "clicked"; })()`);
      while (then === "clicked" && Date.now() < deadline) {
        if (await evaluate(`document.querySelector(${JSON.stringify(step.then.until)}) !== null`)) then = "reached";
        else await new Promise((resolve) => setTimeout(resolve, 100));
      }
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
      pageHeaders,
      then,
      userAgent: await evaluate("navigator.userAgent"),
      liveRegion: { atLoad: liveRegionAtLoad, atEnd: await evaluate(ANSWER_LIVE_REGION) },
      keyboard: keys,
      // Every citation the answer prints: a permalink is printed as code, and nothing else printed as
      // code begins with a slash (digests are hex, IRIs are absolute).
      citations: await evaluate("[...new Set([...document.querySelectorAll('[data-live-answer] code')].map((node) => node.textContent.trim()).filter((text) => text.startsWith('/')))]"),
      // Every quotation the answer shows, with what its article's element prints beside it.
      paint: await evaluate(PAINT_ONLY),
      quotes: await evaluate("[...document.querySelectorAll('[data-live-answer] [data-article]')].filter((node) => node.querySelector('blockquote') !== null).map((node) => ({ article: node.dataset.article, codes: [...node.querySelectorAll('code')].map((code) => code.textContent.trim()) }))"),
    };
  } finally {
    chrome.kill();
    await new Promise((resolve) => setTimeout(resolve, 500));
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
}

/** Asks the API's `verify` for each citation, as a reader checking one would, and keeps what it said. */
async function verifyCitations(origin, citations) {
  const verifications = [];
  for (const identifier of citations) {
    const answer = await fetch(`${origin}/api/v3/verify`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: "verify", parameters: { identifier } }),
    });
    const envelope = await answer.json();
    const value = envelope.result?.value ?? null;
    verifications.push({
      identifier,
      refusal: envelope.refusal?.code ?? (value === null ? `HTTP ${answer.status}` : null),
      verdict: value?.verdict ?? null,
      stateSha256: value?.state_sha256 ?? null,
      requestedAnchor: value?.requested_anchor ?? null,
    });
  }
  return verifications;
}

async function run(apiOutput, mount, expected, browser, liveRoot) {
  const api = await startApi(apiOutput, mount, expected.servedByApi ? liveRoot : null);
  const live = expected.servedByApi ? null : createLiveServer({ root: liveRoot, apiOrigin: api.origin });
  const pageOrigin = live === null ? api.origin : await listen(live);
  try {
    const observed = await observe(browser, pageOrigin, expected.step, { keyboard: expected.keyboard === true });
    const fileEvents = await api.fileWatch.stop();
    const filesAtEnd = await listFiles(api.home);
    const changedFiles = [...filesAtEnd].filter(([path, facts]) => api.filesAtStart.get(path) !== facts).map(([path]) => path);
    observed.api = { startup: api.output().slice(0, api.outputAtStart), output: api.output().slice(api.outputAtStart), changedFiles, fileEvents };
    // Asked once the run's recording is closed, so checking the citations is not the run's traffic.
    observed.verifications = await verifyCitations(api.origin, observed.citations.filter((citation) => PINNED_PERMALINK.test(citation)));
    return { observed, failures: journeyVerdict(observed, { ...expected, origin: pageOrigin }) };
  } finally {
    if (live !== null) await new Promise((resolve) => live.close(resolve));
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
  const servedByApi = argv.includes("--served-by-api");
  const keyboard = argv.includes("--keyboard");
  const { coverage, search, dossier, reading, history, compare, radar, export: exporting } = JOURNEY_STEPS;
  const results = [
    ["coverage, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: coverage, state: "success", corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 }, browser, liveRoot)],
    ["coverage, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: coverage, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["search, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: search, state: "success", texts: [`“${SEARCH_PHRASE}” in fra: 4 with the exact phrase, 1 with every word, in 1 work.`, "art_15 in", "The first hits in the stated order, not the best hits."] }, browser, liveRoot)],
    ["search, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: search, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["dossier, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: dossier, state: "success", texts: [journeyMount.work_key, "1 state, from 2024-02-01 to 2024-02-01.", "What this dossier does not hold"] }, browser, liveRoot)],
    ["dossier, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: dossier, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["reading, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: reading, state: "success", texts: ["the state applying from 2024-02-01", "49 articles quoted", "Art. 15.", "Text served under agreed_same_run_cc_by"] }, browser, liveRoot)],
    ["reading, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: reading, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["history, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: history, state: "success", texts: [`${HISTORY_ANCHOR} in loi-1991-08-10-n3`, "Carried by 1 held state, from 2024-02-01", "first held wording"] }, browser, liveRoot)],
    ["history, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: history, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["compare, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: compare, state: "success", texts: [`loi-1991-08-10-n3: ${READING_DATE} against ${READING_DATE}.`, "The same version applied on both dates."] }, browser, liveRoot)],
    ["compare, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: compare, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["radar, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: radar, state: "success", texts: [`${READING_DATE} to ${READING_DATE}: 1 state of 1 work, of 1 held.`, "not compared: the first state this index holds"] }, browser, liveRoot)],
    ["radar, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: radar, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
    ["export, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, step: exporting, state: "success", texts: ["1 article pinned: 1 exported with text, 0 excluded.", EXPORT_WATERMARK, "Text served under agreed_same_run_cc_by."] }, browser, liveRoot)],
    ["export, with no mount", await run(apiOutput, null, { servedByApi, keyboard, step: exporting, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)],
  ];
  let failed = false;
  for (const [label, { observed, failures }] of results) {
    const toApi = observed.requests.filter((request) => new URL(request.url).pathname.startsWith("/api/")).length;
    console.log(`${label}: ${observed.answerState}; ${observed.requests.length} requests (${toApi} to the API); ` +
      `console ${observed.console.length}; hydration ${observed.hydrated}; ${observed.verifications?.length ?? 0} citations verified; ` +
      `${observed.paint?.painted ?? 0} painted elements, ${observed.paint?.unnamed.length ?? 0} wordless; ` +
      `${observed.keyboard ? `${observed.keyboard.keyPresses} of ${observed.keyboard.characters} characters typed by key; ` : ""}` +
      `${failures.length === 0 ? "PASS" : "FAIL"}`);
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
