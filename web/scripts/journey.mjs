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
// `no_corpus_mounted`. Three EU steps ask for the GDPR by its CELEX: the search page with English chosen
// in the form's language select (`EU_SEARCH_STEP`), the dossier page (`EU_DOSSIER_STEP`), and the
// reading page on the GDPR's wording date (`EU_READING_STEP`). They need an EU index, so on the fixture
// mount (Luxembourg only) the search and dossier steps must end in the refusal card `no_corpus_mounted`
// naming the EU index, and on a real mount whose build report names an EU index each of the three must
// end in the API's answer, every EU citation pinned and verified. In every run, what the
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
//                            [--real-mount]
//
// With `--real-mount` the mount is a real build's (`v3-corpus` with its `build-report.json`), whose
// contents are not known in advance: each of the eight steps, and the two EU steps when the build
// report names an EU index, asks the API its page's request first and holds the page to that answer, a
// success or that refusal by its code, and to every invariant below. The coverage page must name the
// corpus and Luxembourg index the build report records.
//
// With `--keyboard` every form step is driven by the keyboard alone (the launch contract's keyboard
// path): from the top of the page, Tab until each text field has focus, type, Enter to submit; a step
// that chooses an option reaches the select by Tab and types the first letter of the option's label,
// then Tabs on to the submit button and presses Enter; the export composer's pin is reached by Tab and
// checked with Space. Every focus stop must show a focus
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
// state (for an EU citation, that very wording, answered by the EU index), and the article it names
// (an EU provision as `verify` names it, unescaped).
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
import { Session, findBrowser, launchBrowser } from "./browser-evidence.mjs";
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

/** The EU work the EU search step names: the GDPR, by its CELEX. */
export const EU_SEARCH_IDENTIFIER = "32016R0679";

/** The phrase the EU search step types: the GDPR's held English wording carries it in many articles. */
export const EU_SEARCH_PHRASE = "personal data";

/** The language the EU search step chooses in the form's select: the one its held wording is in. */
export const EU_SEARCH_LANGUAGE = Object.freeze({ value: "eng", label: "English" });

/**
 * A hash-pinned Luxembourg permalink, as `verify` takes it: the stable coordinate, `--` and the state
 * digest, and an article id after `#` (the grammar `V3CitationVerificationTests` holds the served
 * answers to).
 */
export const PINNED_PERMALINK = /^\/lu-legilux\/[a-z0-9_-]+\/\d{4}-\d{2}-\d{2}--([0-9a-f]{64})(?:#([^#\s]+))?$/;

/**
 * A hash-pinned EU permalink, as `verify` takes it: the CELEX, the language, the wording date, `--` and
 * the wording digest, and a provision after `#`, escaped as the platform escapes it (#850).
 */
export const PINNED_EU_PERMALINK = /^\/eu-eurlex\/[^/#\s]+\/[a-z]{3}\/\d{4}-\d{2}-\d{2}--([0-9a-f]{64})(?:#([^#\s]+))?$/;

/**
 * What a citation pins, or null when it pins nothing: its publisher, the digest (a Luxembourg state's
 * or an EU wording's) and the article, as `verify` names it (an EU provision unescaped).
 */
export function pinnedCitation(citation) {
  const luxembourg = PINNED_PERMALINK.exec(citation);
  if (luxembourg !== null) return { publisher: "lu-legilux", digest: luxembourg[1], anchor: luxembourg[2] ?? null };
  const europe = PINNED_EU_PERMALINK.exec(citation);
  if (europe === null) return null;
  try {
    return { publisher: "eu-eurlex", digest: europe[1], anchor: europe[2] === undefined ? null : decodeURIComponent(europe[2]) };
  } catch {
    return null;
  }
}

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
    // Once the reading has answered: pin the first three articles (journey J4, an answer across several
    // provisions), and the export must be composed.
    then: Object.freeze({ click: "input[data-pin]", count: 3, until: "[data-export-state=composed]" }),
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
 * The EU search step: the search page asked for the GDPR by its CELEX, with English chosen in the
 * form's language select and a phrase its one held wording carries. It needs an EU index, so it is
 * answered on a mount that holds one (`--real-mount`, when the build report names an EU index), and
 * on the fixture mount (Luxembourg only) and with no mount it must show the refusal
 * `no_corpus_mounted`.
 */
export const EU_SEARCH_STEP = Object.freeze({
  path: "/search.html",
  cites: true,
  operation: "search",
  typed: Object.freeze([EU_SEARCH_PHRASE, EU_SEARCH_IDENTIFIER]),
  chosen: EU_SEARCH_LANGUAGE,
  body: Object.freeze({
    operation_id: "search",
    parameters: Object.freeze({ query: EU_SEARCH_PHRASE, language: EU_SEARCH_LANGUAGE.value, identifier: EU_SEARCH_IDENTIFIER }),
  }),
});

/**
 * The EU dossier step: the dossier page asked for the same EU work by its CELEX, in any held language.
 * Like the EU search it needs an EU index, and where one is held each expression's wording permalink
 * is printed and verified.
 */
export const EU_DOSSIER_STEP = Object.freeze({
  path: "/dossier.html",
  cites: true,
  operation: "dossier",
  typed: EU_SEARCH_IDENTIFIER,
  body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: EU_SEARCH_IDENTIFIER }) }),
});

/** The date the EU reading asks: the GDPR's Formex act date, the date of the one wording an EU index holds of it. */
export const EU_READING_DATE = "2016-04-27";

/**
 * The EU reading step: the reading page asked for the same EU work by its CELEX on its wording date, in any
 * held language. Where an EU index holds it, the original wording is quoted with Decision 95's
 * acknowledgement, and every article permalink the page prints is verified.
 */
export const EU_READING_STEP = Object.freeze({
  path: "/reading.html",
  cites: true,
  operation: "evidence_bundle",
  typed: Object.freeze([EU_SEARCH_IDENTIFIER, EU_READING_DATE]),
  body: Object.freeze({
    operation_id: "evidence_bundle",
    parameters: Object.freeze({ identifier: EU_SEARCH_IDENTIFIER, date: EU_READING_DATE }),
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
 *   texts?, refusalCode?, absentTexts?, mustRefuse?}`; `step` is a `JOURNEY_STEPS` entry and defaults
 *   to coverage. `absentTexts` must appear nowhere on the page, in its text or its markup (whitespace
 *   collapsed); `mustRefuse` is the refusal code the step must end in, whatever the API answered, read
 *   off the refusal card the page shows (`refusalCodes`).
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
      if (keys.placed !== keys.wanted) failures.push(`Tab reached ${keys.placed} of the form's ${keys.wanted} fields`);
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
    const pinned = observed.citations.filter((citation) => pinnedCitation(citation) !== null);
    for (const citation of observed.citations.filter((candidate) => pinnedCitation(candidate) === null)) {
      failures.push(`the page printed ${citation}, which is not a hash-pinned permalink`);
    }
    // An answer that holds nothing to cite (the radar's empty window, a search with no hit) prints none,
    // when the API's own answer holds nothing and the page says so; any other answer of a citing step
    // must cite (the real mount's empty radar window found the first; the review of #815 the second).
    const excused = expected.nothingToCite === true && observed.emptyAnswer === true;
    if (step.cites && expected.state === "success" && observed.citations.length === 0 && !excused) {
      failures.push("the page printed no citation");
    }
    if (observed.emptyAnswer === true && expected.state === "success" && expected.nothingToCite !== true) {
      failures.push("the page says it holds nothing to cite, and the API's answer holds something");
    }
    if (observed.verifications !== undefined) {
      const verified = new Set(observed.verifications.map((check) => check.identifier));
      for (const citation of pinned.filter((candidate) => !verified.has(candidate))) failures.push(`${citation} was not verified`);
      for (const check of observed.verifications) {
        const { publisher = null, digest = null, anchor = null } = pinnedCitation(check.identifier) ?? {};
        // A Luxembourg citation pins a state, an EU citation the one wording held (#850).
        const [pinnedKind, named] = publisher === "eu-eurlex" ? ["wording", check.wordingSha256] : ["state", check.stateSha256];
        if (check.refusal !== null) failures.push(`verify refused ${check.identifier} with ${check.refusal}`);
        else if (check.verdict !== "digest_matches") failures.push(`verify found ${check.identifier} ${check.verdict}, not digest_matches`);
        else if (check.publisher !== undefined && check.publisher !== publisher) failures.push(`verify of ${check.identifier} answered for ${check.publisher}`);
        else if (named !== digest) failures.push(`verify of ${check.identifier} named the ${pinnedKind} ${named}`);
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
      // Either publisher's grammar: a Luxembourg state permalink or an EU wording permalink (review of #903: the
      // Luxembourg-only rule failed every EU quote).
      if (!quote.codes.some((code) => pinnedCitation(code)?.anchor === quote.article)) {
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
    const fileEvents = observed.api.fileEvents ?? [];
    const watchErrors = fileEvents.filter(([event]) => event === "error").map(([, message]) => message);
    if (watchErrors.length > 0) failures.push(`the file watch failed, so the run cannot say the API touched no file: ${watchErrors.join("; ")}`);
    const touched = [...new Set(fileEvents.filter(([event]) => event !== "error").map(([, path]) => path))];
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
  // The code the page's refusal card shows, not a mention of it anywhere on the page (review of #834).
  const shownCodes = Array.isArray(observed.refusalCodes) ? observed.refusalCodes : null;
  if (expected.state === "refusal" && shownCodes !== null && !(shownCodes.length === 1 && shownCodes[0] === expected.refusalCode)) {
    failures.push(`the page's refusal card shows ${JSON.stringify(shownCodes)}, not ${expected.refusalCode}`);
  }
  if (expected.mustRefuse !== undefined) {
    if (!(expected.state === "refusal" && expected.refusalCode === expected.mustRefuse)) {
      failures.push(`the step must refuse ${expected.mustRefuse}, and it was held to ${expected.state === "refusal" ? `the refusal ${expected.refusalCode}` : "an answer"}`);
    }
    if (!(shownCodes !== null && shownCodes.length === 1 && shownCodes[0] === expected.mustRefuse)) {
      failures.push(`the step must show the refusal card ${expected.mustRefuse}, and it shows ${JSON.stringify(shownCodes)}`);
    }
  }
  const collapse = (text) => (text ?? "").replace(/\s+/g, " ");
  const [pageText, pageHtml] = [collapse(observed.text), collapse(observed.html)];
  for (const text of expected.absentTexts ?? []) {
    const words = collapse(text);
    if (pageText.includes(words)) failures.push(`the page shows withheld text "${words}"`);
    else if (pageHtml.includes(words)) failures.push(`the page's markup carries withheld text "${words}"`);
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
 *
 * A watch that fails is reported as an `["error", message]` event, which the verdict says, instead of an
 * unhandled error that ends the whole process: on Linux a recursive watch walks the directories under it,
 * and one removed while it walks fails it (`ENOENT ... scandir`, the base's push run of 67bae40e, where the
 * API's directory was removed while its watch was still open). `stop()` may be called more than once, and
 * answers the same events each time.
 */
export function watchFiles(root) {
  const events = [];
  const watcher = watch(root, { recursive: true }, (event, name) => { events.push([event, String(name ?? "")]); });
  watcher.on("error", (error) => { events.push(["error", String(error?.message ?? error)]); });
  let stopped = null;
  return {
    stop() {
      stopped ??= (async () => {
        // The system reports a change after it happens; a short wait lets the last ones arrive.
        await new Promise((resolve) => setTimeout(resolve, 250));
        watcher.close();
        return events;
      })();
      return stopped;
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

export async function startApi(apiOutput, mount, webRoot = null) {
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
        const filesAtStart = await listFiles(home);
        const fileWatch = watchFiles(home);
        return {
          origin, child, stderr: () => stderr, output: () => output, outputAtStart: output.length, fileWatch,
          /** The files under the API's directory that were added or changed since it first answered. */
          async changedFiles() {
            const filesAtEnd = await listFiles(home);
            return [...filesAtEnd].filter(([path, facts]) => filesAtStart.get(path) !== facts).map(([path]) => path);
          },
          async close() {
            // The watch ends before its directory is removed, whether or not the run stopped it (a run that
            // threw, or the API journeys, which watch nothing): removing a watched directory can fail its watch.
            await fileWatch.stop();
            child.kill();
            await new Promise((resolve) => setTimeout(resolve, 500));
            await rm(home, { recursive: true, force: true }).catch(() => {});
          },
        };
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

/** Sets the form's select to a value as a pointer would: the value, then the change event the page listens for. */
const chooseInSelect = (value) => `(() => {
  const select = document.querySelector('form[role=search] select');
  Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(select, ${JSON.stringify(value)});
  select.dispatchEvent(new Event('change', { bubbles: true }));
  return select.value;
})()`;

/**
 * A form step's action: once hydrated, type into the form's text fields in order (one text, or one per
 * field), choose the option a step names in the form's select, and submit it.
 */
async function typeAndSubmit(session, sessionId, evaluate, deadline, typed, chosen) {
  while (Date.now() < deadline && (await evaluate("document.documentElement.dataset.hydrated ?? null")) === null) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  const texts = Array.isArray(typed) ? typed : [typed];
  for (const [index, text] of texts.entries()) {
    await evaluate(`document.querySelectorAll('form[role=search] input')[${index}].focus()`);
    await session.send("Input.insertText", { text }, sessionId);
  }
  if (chosen !== undefined) await evaluate(chooseInSelect(chosen.value));
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
 * fields (text or search) has focus in turn, type into it key by key, and, when the step chooses an
 * option, Tab to the form's select and type the first letter of the option's label, as a keyboard user
 * picks from a closed select. Then press Enter in a text field, which submits the form as a keyboard
 * user submits it, or, with focus past the text fields, Tab on to the submit button and press Enter on
 * it. Every focus stop on the way is recorded with whether it shows a focus indicator, and the page
 * counts the character keys it received.
 */
async function keyboardTypeAndSubmit(session, sessionId, evaluate, deadline, typed, chosen) {
  while (Date.now() < deadline && (await evaluate("document.documentElement.dataset.hydrated ?? null")) === null) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  await evaluate("document.activeElement && document.activeElement.blur(); window.scrollTo(0, 0)");
  await evaluate(COUNT_CHARACTER_KEYS);
  const texts = Array.isArray(typed) ? typed : [typed];
  const wanted = texts.length + (chosen === undefined ? 0 : 1);
  const isText = (focused) => focused?.inForm && focused.tag === "input" && (focused.type === "text" || focused.type === "search");
  const stops = [];
  let typedFields = 0;
  let picked = chosen === undefined;
  let characters = 0;
  let focused = null;
  for (let tab = 0; tab < 40 && typedFields + (picked && chosen !== undefined ? 1 : 0) < wanted; tab += 1) {
    await pressKey(session, sessionId, "Tab");
    focused = await evaluate(FOCUSED);
    if (focused === null) continue;
    stops.push(focused);
    if (isText(focused) && typedFields < texts.length) {
      await typeByKeys(session, sessionId, texts[typedFields]);
      characters += [...texts[typedFields]].length;
      typedFields += 1;
    } else if (!picked && focused.inForm && focused.tag === "select") {
      await typeByKeys(session, sessionId, chosen.label.charAt(0));
      characters += 1;
      picked = (await evaluate("document.activeElement.value")) === chosen.value;
    }
  }
  const placed = typedFields + (picked && chosen !== undefined ? 1 : 0);
  if (placed === wanted) {
    for (let tab = 0; tab < 10 && !isText(focused) && !(focused?.inForm && focused.tag === "button" && focused.type === "submit"); tab += 1) {
      await pressKey(session, sessionId, "Tab");
      focused = await evaluate(FOCUSED);
      if (focused !== null) stops.push(focused);
    }
  }
  const keyPresses = await evaluate("window.__journeyCharacterKeys");
  if (placed === wanted) await pressKey(session, sessionId, "Enter");
  return { stops, placed, wanted, characters, keyPresses };
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
 * background image, a background colour other than the one it sits on, a visible border, outline or
 * shadow) and saying nothing in words or in an accessible name. The launch contract's "no meaning by
 * colour alone", measured on the page as the browser paints it; decorative elements declared
 * `aria-hidden` are exempt. Borders, outlines and shadows count since the review of #811: an empty
 * span with a red border is a marker as surely as a red square.
 */
export const PAINT_ONLY = `(() => {
  const transparent = (colour) => colour === 'transparent' || colour === 'rgba(0, 0, 0, 0)';
  const edge = (width, style, colour) => parseFloat(width) > 0 && style !== 'none' && style !== 'hidden' && !transparent(colour);
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
      || (!transparent(style.backgroundColor) && style.backgroundColor !== behind(el.parentElement))
      || ['Top', 'Right', 'Bottom', 'Left'].some((side) => edge(style['border' + side + 'Width'], style['border' + side + 'Style'], style['border' + side + 'Color']))
      || edge(style.outlineWidth, style.outlineStyle, style.outlineColor)
      || style.boxShadow !== 'none';
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
  const profile = await mkdtemp(join(tmpdir(), "lex-journey-cdp-"));
  const { child: chrome, url } = await launchBrowser(browser, profile);
  try {
    const session = await Session.open(url);
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
    if (step.typed !== undefined && keyboard) keys = await keyboardTypeAndSubmit(session, sessionId, evaluate, deadline, step.typed, step.chosen);
    else if (step.typed !== undefined) await typeAndSubmit(session, sessionId, evaluate, deadline, step.typed, step.chosen);
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
      // `count` matches are clicked, the first first (journey J4 pins several provisions); one when it is not given.
      const count = step.then.count ?? 1;
      if (keyboard) {
        for (let pinned = 0; pinned < count; pinned += 1) {
          then = await keyboardPin(session, sessionId, evaluate, keys.stops);
          if (then !== "clicked") break;
        }
      } else {
        then = await evaluate(`(() => { const nodes = [...document.querySelectorAll(${JSON.stringify(step.then.click)})].slice(0, ${count}); if (nodes.length < ${count}) return "absent"; for (const node of nodes) node.click(); return "clicked"; })()`);
      }
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
      html: await evaluate("document.documentElement.outerHTML"),
      refusalCodes: await evaluate("[...document.querySelectorAll('.refusal-card .refusal-code')].map((code) => code.textContent.trim())"),
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
      emptyAnswer: await evaluate("document.querySelector('[data-live-answer] [data-no-row], [data-live-answer] [data-no-hit]') !== null"),
      quotes: await evaluate("[...document.querySelectorAll('[data-live-answer] [data-article]')].filter((node) => node.querySelector('blockquote') !== null).map((node) => ({ article: node.dataset.article, codes: [...node.querySelectorAll('code')].map((code) => code.textContent.trim()) }))"),
    };
  } finally {
    chrome.kill();
    await new Promise((resolve) => setTimeout(resolve, 500));
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
}

/**
 * What a page must end in, from what the API answers the same request: an answer is a success, a
 * refusal is that refusal by its code. A real mount's contents are not known in advance, so the run
 * asks the API first and holds the page to its answer (`--real-mount`).
 */
export function expectedFromEnvelope(envelope) {
  if (envelope?.verdict === "answer") {
    // Whether the answer itself holds nothing a page could cite: a search with no hit, a radar window
    // with no row. Only then may the page print no citation (review of #815: the page's own "no hit"
    // is not evidence that there was none).
    const value = envelope.result?.value ?? {};
    const listed = Array.isArray(value.hits) ? value.hits : Array.isArray(value.changes) ? value.changes : null;
    return { state: "success", nothingToCite: listed !== null && listed.length === 0 };
  }
  if (envelope?.verdict === "refuse" && typeof envelope.refusal?.code === "string") return { state: "refusal", refusalCode: envelope.refusal.code };
  throw new Error(`the API answered neither an answer nor a typed refusal: ${JSON.stringify(envelope).slice(0, 200)}`);
}

/** The request a step's page asks: its body, or coverage's, which the page asks as it loads. */
const COVERAGE_BODY = Object.freeze({ operation_id: "coverage", parameters: Object.freeze({}) });

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
      publisher: value?.publisher ?? null,
      stateSha256: value?.state_sha256 ?? null,
      wordingSha256: value?.wording_sha256 ?? null,
      requestedAnchor: value?.requested_anchor ?? null,
    });
  }
  return verifications;
}

/**
 * One step: a server (the API from its build output, or whatever `expected.startServer` starts, such as
 * the release image), the page asked through the browser, and the verdict. A server answers
 * `{ origin, output(), outputAtStart, fileWatch, changedFiles(), close() }`.
 */
export async function run(apiOutput, mount, expected, browser, liveRoot) {
  const api = expected.startServer ? await expected.startServer() : await startApi(apiOutput, mount, expected.servedByApi ? liveRoot : null);
  if (expected.fromApi) {
    // Asked before the browser, of the same server, with the page's own request.
    const body = expected.step.body ?? COVERAGE_BODY;
    const answer = await fetch(`${api.origin}/api/v3/${body.operation_id}`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
    expected = { ...expected, ...expectedFromEnvelope(await answer.json()) };
  }
  const live = expected.servedByApi ? null : createLiveServer({ root: liveRoot, apiOrigin: api.origin });
  const pageOrigin = live === null ? api.origin : await listen(live);
  try {
    const observed = await observe(browser, pageOrigin, expected.step, { keyboard: expected.keyboard === true });
    const fileEvents = await api.fileWatch.stop();
    const changedFiles = await api.changedFiles();
    observed.api = { startup: api.output().slice(0, api.outputAtStart), output: api.output().slice(api.outputAtStart), changedFiles, fileEvents };
    // Asked once the run's recording is closed, so checking the citations is not the run's traffic.
    observed.verifications = await verifyCitations(api.origin, observed.citations.filter((citation) => pinnedCitation(citation) !== null));
    return { observed, failures: journeyVerdict(observed, { ...expected, origin: pageOrigin }) };
  } finally {
    if (live !== null) await new Promise((resolve) => live.close(resolve));
    await api.close();
  }
}

/**
 * The steps against a real mount (`--real-mount`): each page is held to what the API answers its
 * request, and to every invariant a run checks. The coverage page must name the mounted corpus and
 * Luxembourg index by the digests the mount's build report records. When the build report names an
 * EU index, the EU search step runs too: the GDPR searched by its CELEX, every EU citation pinned and
 * verified (`realMountSteps`).
 */
export function realMountSteps(report) {
  const steps = Object.entries(JOURNEY_STEPS).map(([name, step]) => [name, step]);
  return report.europeIndex
    ? [...steps, ["eu search", EU_SEARCH_STEP], ["eu dossier", EU_DOSSIER_STEP], ["eu reading", EU_READING_STEP]]
    : steps;
}

export async function realMountRuns(apiOutput, mount, options, browser, liveRoot) {
  const report = JSON.parse(await readFile(join(mount, "build-report.json"), "utf8"));
  const runs = [];
  for (const [name, step] of realMountSteps(report)) {
    const digests = name === "coverage" ? { corpusSha256: report.corpus.Sha256, indexSha256: report.luxembourgIndex.Sha256 } : {};
    runs.push([`${name}, with the real mount`, await run(apiOutput, mount, { ...options, step, fromApi: true, ...digests }, browser, liveRoot)]);
  }
  return runs;
}

/**
 * The launch contract's licence-blocked journey: the eight steps on the fixture mount with its member's
 * rights recorded as a licence that does not admit redistribution (`journey-mount.json` names the
 * disposition and passages covering every article's body). Each page is held to what the API answers
 * its request, reading and export must show the refusal card `text_withheld` (rights enforced at
 * compose time), and no page may show, or carry in its markup, any passage of an article.
 */
export async function licenceBlockedRuns(apiOutput, mount, options, browser, liveRoot) {
  const journeyMount = JSON.parse(await readFile(join(mount, "journey-mount.json"), "utf8"));
  const absentTexts = journeyMount.withheld_passages ?? [];
  if (absentTexts.length === 0) throw new Error("the licence-blocked mount names no withheld text to look for");
  const runs = [];
  for (const [name, step] of Object.entries(JOURNEY_STEPS)) {
    const mustRefuse = name === "reading" || name === "export" ? { mustRefuse: "text_withheld" } : {};
    const digests = name === "coverage" ? { corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 } : {};
    runs.push([`${name}, with the licence-blocked mount`, await run(apiOutput, mount, { ...options, step, fromApi: true, absentTexts, ...mustRefuse, ...digests }, browser, liveRoot)]);
  }
  return runs;
}

/**
 * What each of the eight steps must show on the fixture mount (`journey-mount.json`), as `[step name,
 * expected]`: every page answers, with the texts the fixture's one work gives it.
 */
export function fixtureMountExpectations(journeyMount) {
  const { coverage, search, dossier, reading, history, compare, radar, export: exporting } = JOURNEY_STEPS;
  return [
    ["coverage", { step: coverage, state: "success", corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 }],
    ["search", { step: search, state: "success", texts: [`“${SEARCH_PHRASE}” in fra: 4 with the exact phrase, 1 with every word, in 1 work.`, "art_15 in", "The first hits in the stated order, not the best hits."] }],
    ["dossier", { step: dossier, state: "success", texts: [journeyMount.work_key, "1 state, from 2024-02-01 to 2024-02-01.", "What this dossier does not hold"] }],
    ["reading", { step: reading, state: "success", texts: ["the state applying from 2024-02-01", "49 articles quoted", "Art. 15.", "Text served under agreed_same_run_cc_by"] }],
    ["history", { step: history, state: "success", texts: [`${HISTORY_ANCHOR} in loi-1991-08-10-n3`, "Carried by 1 held state, from 2024-02-01", "first held wording"] }],
    ["compare", { step: compare, state: "success", texts: [`loi-1991-08-10-n3: ${READING_DATE} against ${READING_DATE}.`, "The same version applied on both dates."] }],
    ["radar", { step: radar, state: "success", texts: [`${READING_DATE} to ${READING_DATE}: 1 state of 1 work, of 1 held.`, "not compared: the first state this index holds"] }],
    ["export", { step: exporting, state: "success", texts: ["3 articles pinned: 3 exported with text, 0 excluded.", EXPORT_WATERMARK, "Text served under agreed_same_run_cc_by."] }],
  ];
}

/** Journey J1's refusal: a reading asked for a date before the work's first held state. */
export const EARLY_READING_DATE = "2019-03-15";
export const EARLY_READING_STEP = Object.freeze({
  path: "/reading.html",
  cites: true,
  operation: "evidence_bundle",
  typed: Object.freeze([DOSSIER_IDENTIFIER, EARLY_READING_DATE]),
  body: Object.freeze({
    operation_id: "evidence_bundle",
    parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date: EARLY_READING_DATE }),
  }),
});

/** Journey J2: a citizen's question searched as typed, which no held article carries. */
export const NO_HIT_PHRASE = "combien de jours de congé j'ai le droit quand mon père est décédé";
export const NO_HIT_SEARCH_STEP = Object.freeze({
  path: "/search.html",
  cites: true,
  operation: "search",
  typed: NO_HIT_PHRASE,
  body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: NO_HIT_PHRASE, language: "fra" }) }),
});

/** Journey J5: a law the corpus does not hold, asked for by its name. */
export const UNKNOWN_LAW = "Circulaire CSSF 20/747";
export const UNKNOWN_LAW_STEP = Object.freeze({
  path: "/dossier.html",
  cites: true,
  operation: "dossier",
  typed: UNKNOWN_LAW,
  body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: UNKNOWN_LAW }) }),
});

/**
 * What the specification's journeys the eight steps do not walk must show on the fixture mount
 * (the review pack's `05-user-journeys.md`; J3, J4 and J8's radar are the compare, export and radar steps):
 * - J1, a date before the work's history: the reading refuses `no_version_for_date`, saying no earlier state
 *   is held and where the history begins, never another date's text;
 * - J2, a question no held article carries: the search answers with no hit, says what was matched, and that
 *   this is not evidence that the law does not exist;
 * - J5, a law the corpus does not hold: the dossier refuses `identifier_unknown`, with the population this
 *   build searched and the same absence note.
 */
export function specificationJourneyExpectations() {
  const absence = "It is not evidence that the instrument or the law does not exist.";
  return [
    ["J1, a date before the history", { step: EARLY_READING_STEP, state: "refusal", refusalCode: "no_version_for_date", texts: ["No earlier state is held: the requested date precedes this history.", "2024-02-01"] }],
    ["J2, a question no article carries", { step: NO_HIT_SEARCH_STEP, state: "success", nothingToCite: true, texts: ["0 with the exact phrase, 0 with every word, in 0 works.", absence] }],
    ["J5, a law not held", { step: UNKNOWN_LAW_STEP, state: "refusal", refusalCode: "identifier_unknown", texts: ["This build's Luxembourg index holds 1 Luxembourg work, with states dated from 2024-02-01 to 2024-02-01.", absence] }],
  ];
}

/** The MCP protocol revision the API states on every MCP answer (`V3McpJsonRpc.ProtocolVersion`). */
export const MCP_PROTOCOL_VERSION = "2025-06-18";

/**
 * An envelope without the two fields that name the request and the moment it was answered, which differ for
 * any two requests (`request_ref`, a digest of the server's trace id; `context.freshness.observed_at`, its
 * clock): what REST and MCP must answer identically.
 */
export function withoutRequestFields(envelope) {
  const copy = structuredClone(envelope ?? null);
  if (copy !== null && typeof copy === "object") {
    delete copy.request_ref;
    if (copy.context?.freshness !== undefined) delete copy.context.freshness.observed_at;
  }
  return copy;
}

/** The first path at which two JSON values differ, for a failure that says where. */
function firstDifference(left, right, path = "") {
  if (Object.is(left, right)) return null;
  if (typeof left !== "object" || typeof right !== "object" || left === null || right === null || Array.isArray(left) !== Array.isArray(right)) {
    return path || "(the root)";
  }
  for (const key of new Set([...Object.keys(left), ...Object.keys(right)])) {
    const found = firstDifference(left[key], right[key], `${path}${Array.isArray(left) ? `[${key}]` : `.${key}`}`);
    if (found !== null) return found;
  }
  return null;
}

/**
 * Journey J7, the developer consuming the MCP server: one request answered through REST
 * (`{status, json}`) and as an MCP tool call of the same process (`{status, protocolVersion, json}`), as
 * failures. The tool result's structured content and its text are the REST envelope, apart from the two
 * request fields; the answer states the protocol revision; neither path is an error.
 */
export function envelopeIdentityFailures(rest, mcp) {
  const failures = [];
  if (rest.status !== 200) failures.push(`REST answered HTTP ${rest.status}`);
  if (mcp.status !== 200) failures.push(`MCP answered HTTP ${mcp.status}`);
  if (mcp.protocolVersion !== MCP_PROTOCOL_VERSION) failures.push(`MCP answered protocol ${mcp.protocolVersion ?? "none"}, not ${MCP_PROTOCOL_VERSION}`);
  const result = mcp.json?.result;
  if (mcp.json?.error !== undefined || result === undefined) {
    failures.push(`MCP answered no tool result: ${JSON.stringify(mcp.json?.error ?? mcp.json).slice(0, 200)}`);
    return failures;
  }
  if (result.isError !== false) failures.push(`the tool result says isError ${result.isError}`);
  let text;
  try {
    text = JSON.parse(result.content?.[0]?.text ?? "");
  } catch {
    failures.push("the tool result's text is not the envelope as JSON");
  }
  if (text !== undefined && firstDifference(text, result.structuredContent) !== null) {
    failures.push(`the tool result's text and its structured content differ at ${firstDifference(text, result.structuredContent)}`);
  }
  const difference = firstDifference(withoutRequestFields(result.structuredContent), withoutRequestFields(rest.json));
  if (difference !== null) failures.push(`MCP and REST answered different envelopes, first at ${difference}`);
  return failures;
}

/** Every request the pages of the journeys make: the eight steps', J1's, J2's and J5's, each once. */
export function pageRequestBodies() {
  const bodies = [...Object.values(JOURNEY_STEPS), EARLY_READING_STEP, NO_HIT_SEARCH_STEP, UNKNOWN_LAW_STEP].map((step) => step.body ?? COVERAGE_BODY);
  return [...new Map(bodies.map((body) => [JSON.stringify(body), body])).values()];
}

async function postJson(url, body) {
  const answer = await fetch(url, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
  const text = await answer.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {
    // Not JSON: the failure names the status.
  }
  return { status: answer.status, protocolVersion: answer.headers.get("mcp-protocol-version"), json };
}

/**
 * Journey J8, monitoring, at the API: the event log polled as a client polls it, as failures. The same
 * request answers the same events (the log is append-only); from its own `next_after` it answers nothing more;
 * a cursor from another log is refused `snapshot_unknown`, never read as this log's; every event's permalink
 * verifies; and `answer_drift` on a genesis log names no invalidated answer and asserts no absence of drift.
 */
export async function eventsFailures(origin) {
  const failures = [];
  const ask = (operation, parameters) => postJson(`${origin}/api/v3/${operation}`, { operation_id: operation, parameters });
  const first = await ask("events", {});
  const value = first.json?.result?.value;
  if (first.json?.verdict !== "answer" || !Array.isArray(value?.events)) {
    failures.push(`events answered no event list: ${JSON.stringify(first.json).slice(0, 200)}`);
    return failures;
  }
  if (value.events.length === 0) failures.push("the fixture mount's log holds no event to poll");
  const again = await ask("events", {});
  const repeated = firstDifference(withoutRequestFields(first.json), withoutRequestFields(again.json));
  if (repeated !== null) failures.push(`the same events request answered differently, first at ${repeated}`);
  if (value.has_more === false) {
    const after = await ask("events", { after: value.next_after });
    const rest = after.json?.result?.value;
    if (!Array.isArray(rest?.events) || rest.events.length !== 0 || rest.has_more !== false) {
      failures.push(`from its own next_after the log answered more: ${JSON.stringify(after.json).slice(0, 200)}`);
    }
  }
  const foreign = await ask("events", { after: `${"0".repeat(64)}:1` });
  if (foreign.json?.refusal?.code !== "snapshot_unknown") failures.push(`a cursor from another log answered ${foreign.json?.refusal?.code ?? foreign.json?.verdict}, not snapshot_unknown`);
  for (const event of value.events) {
    const checked = await ask("verify", { identifier: event.permalink });
    if (checked.json?.result?.value?.verdict !== "digest_matches") failures.push(`event ${event.seq}'s permalink ${event.permalink} does not verify`);
  }
  const drift = await ask("answer_drift", {});
  const driftValue = drift.json?.result?.value;
  if (!Array.isArray(driftValue?.invalidated_answers) || driftValue.invalidated_answers.length !== 0) {
    failures.push(`answer_drift on a genesis log named invalidated answers: ${JSON.stringify(drift.json).slice(0, 200)}`);
  }
  if (driftValue?.asserts_no_drift_in_law !== false) failures.push("answer_drift asserts no drift in the law, which no log can show");
  return failures;
}

/** Journey J6: a question that must be refused as legal advice, put to `ask` in the reader's own words. */
export const ADVICE_QUESTION = "Mon employeur peut-il me refuser un congé parental si je travaille à temps partiel ?";

/** The operations the contained assistant must hand a reader to instead: the primary text, read deterministically. */
export const ASK_PRIMARY_TEXT_ROUTES = Object.freeze(["resolve", "as_of", "evidence_bundle"]);

/**
 * Journey J6 at the API, as failures: `ask` answers the contained assistant's card (Decision 91), never a legal
 * conclusion. The verdict is `point`, the presentation `assistant_v3_unavailable`, the question is not read, the
 * model gloss is disabled, and the card hands the reader to the deterministic operations that deliver the
 * primary text, each on its own served route; nothing of the question is in the card.
 */
export function askCardFailures(envelope) {
  const failures = [];
  if (envelope?.verdict !== "point") failures.push(`ask answered the verdict ${envelope?.verdict}, not point`);
  const value = envelope?.result?.value;
  if (value?.presentation_result !== "assistant_v3_unavailable") failures.push(`ask presented ${value?.presentation_result}, not assistant_v3_unavailable`);
  if (value?.question_read !== false) failures.push("the ask card does not say the question was not read");
  if (value?.containment?.model_gloss !== "disabled") failures.push(`the model gloss is ${value?.containment?.model_gloss}, not disabled`);
  const actions = Array.isArray(value?.deterministic_actions) ? value.deterministic_actions : [];
  for (const operation of ASK_PRIMARY_TEXT_ROUTES) {
    if (!actions.some((action) => action.operation_id === operation)) failures.push(`the ask card does not hand the reader to ${operation}`);
  }
  for (const action of actions) {
    if (action.route !== `/api/v3/${action.operation_id}`) failures.push(`the action ${action.operation_id} names the route ${action.route}`);
  }
  if (JSON.stringify(value ?? null).includes(ADVICE_QUESTION)) failures.push("the ask card carries the question's words");
  return failures;
}

/**
 * Journeys J6, J7 and J8 at the API, against one API process over the mount: `ask` answered as the contained
 * assistant's card; each page request, and ask, events and answer drift, through REST and MCP; then the event log
 * polled. Returns `[name, failures]` pairs.
 */
export async function apiJourneyRuns(apiOutput, mount) {
  const api = await startApi(apiOutput, mount);
  try {
    const results = [];
    const ask = { operation_id: "ask", parameters: { question: ADVICE_QUESTION } };
    results.push(["J6, a question that must be refused as legal advice", askCardFailures((await postJson(`${api.origin}/api/v3/ask`, ask)).json)]);
    for (const body of [...pageRequestBodies(), ask, { operation_id: "events", parameters: {} }, { operation_id: "answer_drift", parameters: {} }]) {
      const rest = await postJson(`${api.origin}/api/v3/${body.operation_id}`, body);
      const mcp = await postJson(`${api.origin}/mcp`, {
        jsonrpc: "2.0", id: body.operation_id, method: "tools/call", params: { name: body.operation_id, arguments: body.parameters },
      });
      results.push([`J7, ${body.operation_id} ${JSON.stringify(body.parameters)} through REST and MCP`, envelopeIdentityFailures(rest, mcp)]);
    }
    results.push(["J8, the event log polled and answer drift", await eventsFailures(api.origin)]);
    return results;
  } finally {
    await api.close();
  }
}

/**
 * The two-state mount (`journey-mount.json` names `later_date`): the fixture's work with a later state in which
 * one article is amended. Journey J3 compares the two states and journey J8's radar lists the later state compared
 * with the one it replaced, each showing that change (review of #915: the one-state fixture shows neither).
 */
export function twoStateExpectations(journeyMount) {
  const { first_date: first, later_date: later, amended_article: amended } = journeyMount;
  const compare = Object.freeze({
    ...JOURNEY_STEPS.compare,
    typed: Object.freeze([DOSSIER_IDENTIFIER, first, later]),
    body: Object.freeze({ operation_id: "diff", parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date_from: first, date_to: later }) }),
  });
  const radar = Object.freeze({
    ...JOURNEY_STEPS.radar,
    typed: Object.freeze([first, later]),
    body: Object.freeze({ operation_id: "changes_in_period", parameters: Object.freeze({ date_from: first, date_to: later }) }),
  });
  const counts = "1 changed, 0 added, 0 removed";
  return [
    ["J3, two states compared", { step: compare, state: "success", texts: [`${journeyMount.work_key.split("/").pop()}: ${first} against ${later}.`, counts, `${amended}: changed`] }],
    ["J8, the period's change on the radar", { step: radar, state: "success", texts: [`wording changed from the state of ${first}`, `: ${counts}`] }],
  ];
}

/**
 * The EU annex control mount (`journey-mount.json` names `eu_annex`): the reading and dossier pages for the act whose
 * annex the publisher's PDF holds only as images. Each page must say that the annex is not served as text, and neither
 * the annex's text nor its title may appear anywhere on the page: the launch contract's annex line, walked in a browser.
 */
export function europeAnnexExpectations(journeyMount) {
  const { celex, wording_date: date, annexes, absent_texts: absentTexts } = journeyMount.eu_annex;
  if (!Array.isArray(absentTexts) || absentTexts.length === 0) throw new Error("the EU annex mount names no annex text to look for");
  const reading = Object.freeze({
    ...EU_READING_STEP,
    typed: Object.freeze([celex, date]),
    body: Object.freeze({ operation_id: "evidence_bundle", parameters: Object.freeze({ identifier: celex, date }) }),
  });
  const dossier = Object.freeze({
    ...EU_DOSSIER_STEP,
    typed: celex,
    body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: celex }) }),
  });
  const line = annexes === 1
    ? "1 annex of the English wording is not served as text, and is never searched, quoted or exported"
    : `${annexes} annexes of the English wording are not served as text, and are never searched, quoted or exported`;
  return [
    ["the annex control case read on its wording date", { step: reading, state: "success", texts: [line], absentTexts }],
    ["the annex control case's dossier", { step: dossier, state: "success", texts: [line], absentTexts }],
  ];
}

/**
 * The EU search step on the fixture mount, which holds no EU index: the page must show the refusal card
 * `no_corpus_mounted`, naming the EU index as the one missing.
 */
export const EU_SEARCH_ON_FIXTURE = Object.freeze({
  step: EU_SEARCH_STEP,
  state: "refusal",
  refusalCode: "no_corpus_mounted",
  texts: Object.freeze(["This build has no EU index mounted."]),
});

/** The EU dossier step on the fixture mount, which holds no EU index: the same refusal card. */
export const EU_DOSSIER_ON_FIXTURE = Object.freeze({ ...EU_SEARCH_ON_FIXTURE, step: EU_DOSSIER_STEP });

/** The eight steps against the fixture mount, each held to `fixtureMountExpectations`. */
export async function fixtureMountRuns(apiOutput, mount, options, browser, liveRoot) {
  const journeyMount = JSON.parse(await readFile(join(mount, "journey-mount.json"), "utf8"));
  const runs = [];
  for (const [name, expected] of fixtureMountExpectations(journeyMount)) {
    runs.push([`${name}, with the fixture mount`, await run(apiOutput, mount, { ...options, ...expected }, browser, liveRoot)]);
  }
  return runs;
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
  const realMount = argv.includes("--real-mount");
  const journeyMount = realMount ? null : JSON.parse(await readFile(join(mount, "journey-mount.json"), "utf8"));
  // A fixture mount that names a rights disposition is the licence-blocked mount; one that names a later date is the
  // two-state mount.
  const licenceBlocked = journeyMount?.rights_disposition !== undefined;
  const twoState = journeyMount?.later_date !== undefined;
  // One that names an EU annex is the image-only annex control mount.
  const europeAnnex = journeyMount?.eu_annex !== undefined;
  // `--live-root` serves a directory built elsewhere instead of building one: how a deliberately
  // broken page is shown to fail the journey.
  const liveRoot = argv.includes("--live-root") ? argument("--live-root") : await buildLive();
  const browser = await findBrowser();
  const servedByApi = argv.includes("--served-by-api");
  const keyboard = argv.includes("--keyboard");
  const results = [];
  if (realMount) results.push(...await realMountRuns(apiOutput, mount, { servedByApi, keyboard }, browser, liveRoot));
  else if (licenceBlocked) results.push(...await licenceBlockedRuns(apiOutput, mount, { servedByApi, keyboard }, browser, liveRoot));
  else if (twoState) {
    for (const [name, expected] of twoStateExpectations(journeyMount)) {
      results.push([`${name}, with the two-state mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
    }
  } else if (europeAnnex) {
    for (const [name, expected] of europeAnnexExpectations(journeyMount)) {
      results.push([`${name}, with the EU annex control mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
    }
  } else {
    // Each step with the fixture mount, then with no mount, where every page shows the refusal card.
    for (const [name, expected] of fixtureMountExpectations(journeyMount)) {
      results.push([`${name}, with the fixture mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
      results.push([`${name}, with no mount`, await run(apiOutput, null, { servedByApi, keyboard, step: expected.step, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)]);
    }
    // The specification's journeys the eight steps do not walk (J1's refusal, J2, J5).
    for (const [name, expected] of specificationJourneyExpectations()) {
      results.push([`${name}, with the fixture mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
    }
    // The EU steps: the fixture mount holds no EU index, so the EU work is refused for the EU corpus.
    results.push(["eu search, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, ...EU_SEARCH_ON_FIXTURE }, browser, liveRoot)]);
    results.push(["eu dossier, with the fixture mount", await run(apiOutput, mount, { servedByApi, keyboard, ...EU_DOSSIER_ON_FIXTURE }, browser, liveRoot)]);
  }
  // Journeys J6, J7 and J8 at the API (no page asks the contained assistant or MCP, or polls events), on the
  // fixture mount.
  const apiResults = realMount || licenceBlocked || twoState || europeAnnex ? [] : await apiJourneyRuns(apiOutput, mount);
  let failed = false;
  for (const [label, failures] of apiResults) {
    console.log(`${label}: ${failures.length === 0 ? "PASS" : "FAIL"}`);
    for (const failure of failures) console.log(`  - ${failure}`);
    failed ||= failures.length > 0;
  }
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
