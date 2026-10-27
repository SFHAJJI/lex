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
// at its own address (a search page with its search after "#", R109, and an act's readable address with the date it
// read the act at in its query, R13: `endAddressesOf`) with no history entry added and no history state written, no cookie set,
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
// The home page (the specification of record's R47, R11, R13 and R50) is held to more (`frontDoor`): it arrives rendered
// by the server from its coverage answer, carrying the coverage line (counted, or with no mount the no-mount sentence)
// and a date field that holds today, the calendar date in Luxembourg, so it asks nothing as it loads; its search sends
// that date (the step's body names it as `TODAY_IN_LUXEMBOURG`, filled with today when the run asks, either side of a
// midnight in Luxembourg during the run); and at 320 by 640 CSS pixels, the box is the first control of the page's
// content and both it and the sentence under it, which says what Lex is not, are in view without scrolling.
//
// The specification of record's R60 has runs of its own on the fixture mount (`failurePageExpectations`): an address
// the site has no page for must arrive as the 404 page, with status 404, and its search must answer as the home page's
// does, or with no mount show the failure panel; and the search with no mount must show the failure panel, whose retry,
// pressed once (by the keyboard on a keyboard run), asks the very same request again.
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
import { EUROPE_TEXT_ACKNOWLEDGEMENT } from "./reading-answer.mjs";
import { TMP_MARGIN_MEBIBYTES, tmpMebibytesFor } from "./image-run.mjs";
import { INTERFACE_LANGUAGES, anchorText, articlePermalink, currentAddress, dateOfQuery, escapeSegment, parseSitePath, sameAddressIn, withDateQuery } from "./permalinks.mjs";
import { searchFragment } from "./search-address.mjs";
import { INDEXED, X_ROBOTS_TAG } from "./search-engines.mjs";
import { CANONICAL_HOST } from "./routes.mjs";
import { luxembourgDate } from "./temporal.mjs";
import { fetchWithinLimit } from "./within-limit.mjs";

/** The origin the site's head links name (`Document.jsx`, `head-links.mjs`), whatever server a journey runs against. */
const SITE_ORIGIN = `https://${CANONICAL_HOST}`;

export const ANSWER_DEADLINE_MS = 30_000;

/** The phrase the search step types: on the fixture mount, 4 strict hits (and 1 relaxed, which the pages do not ask). */
export const SEARCH_PHRASE = "assemblée générale";

/**
 * What the fixture mount's search says, as the English pages say it (the plan's item 4.3): the passages found by the exact
 * phrase alone, the population searched (its one act, version and 49 articles), the version under the act, as published,
 * dated as the publisher dates it, and a passage of art_15.
 */
export const SEARCH_FOUND_SAID = `“${SEARCH_PHRASE}” in French: 4 passages with the exact phrase, in 1 act.`;
export const SEARCH_SEARCHED_SAID = "Searched: 1 act of Luxembourg law in French, in 1 version with 49 articles of searchable text, as of the index built on";
export const SEARCH_VERSION_SAID = "The act as published, applicable from 2024-02-01 (publisher)";

/**
 * The date a home page's search sends, in a step's body: today, the calendar date in Luxembourg when the page asked
 * (R13), which the run fills in with its own today (`bodyOn`).
 */
export const TODAY_IN_LUXEMBOURG = "{today in Luxembourg}";

/** Today in Luxembourg at `now` (milliseconds), by the coded rule the pages and the API use (plan 0.8). */
export function todayInLuxembourgAt(now) {
  return luxembourgDate(new Date(now).toISOString());
}

/** A step's request body with today written where it names `TODAY_IN_LUXEMBOURG`. */
export function bodyOn(body, today) {
  if (body === null || body.parameters === undefined) return body;
  const named = Object.values(body.parameters).includes(TODAY_IN_LUXEMBOURG);
  if (!named) return body;
  return { ...body, parameters: Object.fromEntries(Object.entries(body.parameters).map(([key, value]) => [key, value === TODAY_IN_LUXEMBOURG ? today : value])) };
}

/**
 * Where a step's page must end, for each of the run's todays (`bodyOn`): its own path, and what the page writes into its own
 * address once it has asked (the plan's items 3.4 and 3.5). A search typed into a page that keeps its search in its address
 * (the home and search pages; not the 404 page's box, `notFound`) ends with that search after "#" (`searchFragment`, R109),
 * built from the very request the step must send; an act's readable address ends with the date it read the act at
 * (`?date=`, R13), unless its address keeps one already. Any other step ends where it began.
 */
export function endAddressesOf(step, todays) {
  const [path, hash = null] = step.path.split("#");
  const [pathname, query = null] = path.split("?");
  return [...new Set(todays.map((today) => {
    const parameters = bodyOn(step.body ?? null, today)?.parameters ?? {};
    if (step.operation === "search" && step.typed !== undefined && step.notFound !== true && hash === null) {
      return `${path}${searchFragment({ query: parameters.query, language: parameters.language, identifier: parameters.identifier ?? "", date: parameters.date ?? "" })}`;
    }
    if (parseSitePath(pathname)?.kind === "current" && typeof parameters.date === "string") {
      const search = query === null ? "" : `?${query}`;
      return `${dateOfQuery(search) === null ? `${pathname}${withDateQuery(search, parameters.date)}` : path}${hash === null ? "" : `#${hash}`}`;
    }
    return step.path;
  }))];
}

/** What a page says of itself beside the law it shows, as the English pages say it (R50). */
export const NOT_ADVICE_SAID = "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.";

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

/** The language of the fixture state's text, which its permalink names (R64). */
export const READING_LANGUAGE = "fra";

/**
 * A hash-pinned Luxembourg permalink, as `verify` takes it, in the one shape of R64: the stable coordinate, `--`, the
 * state digest and the text's language, and an article id as a last segment, escaped as the platform escapes it (the
 * grammar `V3CitationVerificationTests` holds the served answers to).
 */
export const PINNED_PERMALINK = /^\/lu-legilux\/[a-z0-9_-]+\/\d{4}-\d{2}-\d{2}--([0-9a-f]{64})\/[a-z]{3}(?:\/([^/#?\s]+))?$/;

/**
 * A hash-pinned EU permalink, as `verify` takes it, in the one shape of R64: the CELEX, the wording date, `--`, the
 * wording digest and the language, and a provision as a last segment, escaped as the platform escapes it (#850).
 */
export const PINNED_EU_PERMALINK = /^\/eu-eurlex\/[^/#\s]+\/\d{4}-\d{2}-\d{2}--([0-9a-f]{64})\/[a-z]{3}(?:\/([^/#?\s]+))?$/;

/**
 * What a citation pins, or null when it pins nothing: its publisher, the digest (a Luxembourg state's
 * or an EU wording's) and the article, as `verify` names it (unescaped).
 */
export function pinnedCitation(citation) {
  for (const [publisher, pattern] of [["lu-legilux", PINNED_PERMALINK], ["eu-eurlex", PINNED_EU_PERMALINK]]) {
    const match = pattern.exec(citation);
    if (match === null) continue;
    try {
      return { publisher, digest: match[1], anchor: match[2] === undefined ? null : decodeURIComponent(match[2]) };
    } catch {
      return null;
    }
  }
  return null;
}

/**
 * The steps for one Luxembourg work: the page each loads, the operation it must ask, and what it does before waiting. A
 * step that `cites` prints at least one citation on an answer. `identifier` is the work, `date` a date one of its states
 * applies on, `anchor` an article id that state holds and `language` that state's text language; `today`, the date in
 * Luxembourg the readable address's page asks (R13, R67), by this machine's clock unless given.
 */
export function journeyStepsFor({ identifier, date, anchor, language = READING_LANGUAGE, today = luxembourgDate(new Date().toISOString()) }) {
  const placeholder = `${identifier}/${date}--${"0".repeat(64)}/${language}`;
  return Object.freeze({
  coverage: Object.freeze({ path: "/en/coverage.html", operation: "coverage", body: null }),
  // The home page at the language's root (R47, R61): the search, from the front door, at today's date in Luxembourg (R13).
  home: Object.freeze({
    path: "/en/",
    cites: true,
    frontDoor: true,
    operation: "search",
    typed: SEARCH_PHRASE,
    body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: SEARCH_PHRASE, language: "fra", mode: "strict", date: TODAY_IN_LUXEMBOURG }) }),
  }),
  search: Object.freeze({
    path: "/en/search.html",
    cites: true,
    operation: "search",
    typed: SEARCH_PHRASE,
    body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: SEARCH_PHRASE, language: "fra", mode: "strict" }) }),
  }),
  dossier: Object.freeze({
    path: "/en/dossier.html",
    cites: true,
    operation: "dossier",
    typed: identifier,
    body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: identifier }) }),
  }),
  reading: Object.freeze({
    path: "/en/reading.html",
    cites: true,
    operation: "evidence_bundle",
    typed: Object.freeze([identifier, date]),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: identifier, date: date }),
    }),
  }),
  compare: Object.freeze({
    path: "/en/compare.html",
    cites: true,
    operation: "diff",
    typed: Object.freeze([identifier, date, date]),
    body: Object.freeze({
      operation_id: "diff",
      parameters: Object.freeze({ identifier: identifier, date_from: date, date_to: date }),
    }),
  }),
  radar: Object.freeze({
    path: "/en/radar.html",
    cites: true,
    operation: "changes_in_period",
    typed: Object.freeze([date, date]),
    body: Object.freeze({
      operation_id: "changes_in_period",
      parameters: Object.freeze({ date_from: date, date_to: date }),
    }),
  }),
  export: Object.freeze({
    path: "/en/export.html",
    cites: true,
    operation: "evidence_bundle",
    typed: Object.freeze([identifier, date]),
    // Once the reading has answered: pin the first three articles (journey J4, an answer across several
    // provisions), and the export must be composed.
    then: Object.freeze({ click: "input[data-pin]", count: 3, until: "[data-export-state=composed]" }),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: identifier, date: date }),
    }),
  }),
  history: Object.freeze({
    path: "/en/history.html",
    cites: true,
    operation: "article_history",
    typed: Object.freeze([identifier, anchor]),
    body: Object.freeze({
      operation_id: "article_history",
      parameters: Object.freeze({ identifier: identifier, anchor: anchor }),
    }),
  }),
  // A work's address (UX spec section 1, URLs; `permalinks.mjs`): the dossier page served at it asks that work's
  // dossier as it loads, nothing typed.
  address: Object.freeze({
    path: `/en${identifier}`,
    rendered: true,
    cites: true,
    operation: "dossier",
    body: Object.freeze({ operation_id: "dossier", parameters: Object.freeze({ identifier: identifier }) }),
  }),
  // A state's permalink at one of its articles, as a search hit links it: the reading page served at it asks that
  // state's reading by the permalink itself (R64), shows it as the state the permalink pins, the cited article first, and
  // brings that article into view. The digest is the mount's, so `run` opens the permalink the same server's dossier names
  // for the date (`pinnedStep`); the placeholder's digest (all zeros) pins nothing, and stays only where no state is held
  // on the date (no mount).
  // The page asks the page of the state that holds the article (the plan's item 3.7: `article`).
  permalink: Object.freeze({
    path: `/en${placeholder}/${escapeSegment(anchor)}`,
    rendered: true,
    pins: Object.freeze({ identifier: identifier, date: date, anchor: anchor, language: language }),
    cites: true,
    operation: "evidence_bundle",
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: placeholder, date: date, language: language, article: anchor }),
    }),
  }),
  // A permalink whose digest pins no held state (all zeros), as a link made before the text was derived again would: the
  // API refuses it pinned_digest_mismatch with the current link (R64), and the page says so above the refusal card, in
  // view, with that link (UX spec section 1: a link cannot silently drift).
  stale: Object.freeze({
    path: `/en${placeholder}/${escapeSegment(anchor)}`,
    rendered: true,
    stale: true,
    cites: true,
    operation: "evidence_bundle",
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: placeholder, date: date, language: language, article: anchor }),
    }),
  }),
  // The work's readable address at one of its articles (R67): the reading page served at it asks the work at today's
  // date in Luxembourg, in the text's language it names, and shows that article first, brought into view.
  current: Object.freeze({
    path: `/en${identifier}/${language}/${escapeSegment(anchor)}`,
    rendered: true,
    current: Object.freeze({ anchor: anchor }),
    cites: true,
    operation: "evidence_bundle",
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier: identifier, date: today, language: language, article: anchor }),
    }),
  }),
});
}

/**
 * The step a permalink opens (`pins`), at the permalink the server's own dossier names for its work and date, as a reader
 * would copy it from there; the step unchanged when the dossier names no one state on that date (no mount), so its
 * placeholder, which pins nothing, is opened. `ask` stands for `askOperation`, so a test can say what the server answers.
 */
export async function pinnedStep(origin, step, { ask = askOperation } = {}) {
  if (step.pins === undefined) return step;
  const { identifier, date, anchor, language } = step.pins;
  const onDate = ((await ask(origin, "dossier", { identifier }))?.result?.value?.states ?? [])
    .filter((state) => state.applicability_date === date && state.language === language);
  if (onDate.length !== 1 || typeof onDate[0].permalink !== "string" || parseSitePath(onDate[0].permalink)?.kind !== "state") return step;
  // The page asks by the permalink it stands at (R64), so the body names the dossier's permalink too.
  const body = step.body === null || step.body === undefined
    ? step.body
    : Object.freeze({ ...step.body, parameters: Object.freeze({ ...step.body.parameters, identifier: onDate[0].permalink }) });
  return Object.freeze({ ...step, path: `/en${articlePermalink(onDate[0].permalink, anchor)}`, body });
}

/**
 * Every citation an answer prints, from the text of its `code` elements and the targets of its links: each permalink
 * printed as code (nothing else printed as code begins with a slash: digests are hex, IRIs are absolute), and each link
 * to a state's or an EU wording's reading (`permalinks.mjs`), under any interface's path, as the permalink it opens. A
 * search hit is such a link, its row printing no digest (UX spec section 2).
 */
export function answerCitations(codes, hrefs) {
  // A link's article is a segment of its address (R64); a position inside it, after `#`, is no part of the citation.
  const linked = hrefs.flatMap((href) => {
    const hash = href.indexOf("#");
    const named = parseSitePath(hash < 0 ? href : href.slice(0, hash));
    return named?.kind === "state" ? [named.address] : [];
  });
  return [...new Set([...codes.filter((text) => text.startsWith("/")), ...linked])];
}

/** The steps on the fixture mount: its one work, on its state's date. */
export const JOURNEY_STEPS = journeyStepsFor({ identifier: DOSSIER_IDENTIFIER, date: READING_DATE, anchor: HISTORY_ANCHOR });

/**
 * The EU search step: the search page asked for the GDPR by its CELEX, with English chosen in the
 * form's language select and a phrase its one held wording carries. It needs an EU index, so it is
 * answered on a mount that holds one (`--real-mount`, when the build report names an EU index), and
 * on the fixture mount (Luxembourg only) and with no mount it must show the refusal
 * `no_corpus_mounted`.
 */
export const EU_SEARCH_STEP = Object.freeze({
  path: "/en/search.html",
  cites: true,
  operation: "search",
  typed: Object.freeze([EU_SEARCH_PHRASE, EU_SEARCH_IDENTIFIER]),
  chosen: EU_SEARCH_LANGUAGE,
  body: Object.freeze({
    operation_id: "search",
    parameters: Object.freeze({ query: EU_SEARCH_PHRASE, language: EU_SEARCH_LANGUAGE.value, mode: "strict", identifier: EU_SEARCH_IDENTIFIER }),
  }),
});

/**
 * The EU dossier step: the dossier page asked for the same EU work by its CELEX, in any held language.
 * Like the EU search it needs an EU index, and where one is held each expression's wording permalink
 * is printed and verified.
 */
export const EU_DOSSIER_STEP = Object.freeze({
  path: "/en/dossier.html",
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
  path: "/en/reading.html",
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
  // A page that never arrived (the browser's own error page, as when the server is down or refuses the connection) is one
  // failure, named, rather than every check below failing, or throwing, on what it cannot read.
  if (/^(?:chrome-error|about):/.test(observed.location ?? "")) return [`the page did not load: the browser stands at ${observed.location}`];
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
  // A permalink's page shows the state the permalink pins (no notice that another version stands in for it), the cited
  // article first, and brings it into view (R64); a readable address's page shows the article first too (R67). A stale
  // permalink is refused pinned_digest_mismatch, and its page says, in view, that the link pins no version held at its
  // date, with the current link.
  const cites = step.pins?.anchor ?? step.current?.anchor;
  if (expected.state === "success" && cites !== undefined) {
    if (step.pins !== undefined && observed.pinReplaced !== false) failures.push(`the page says the permalink's state is not the one served (${observed.pinReplaced ?? "not observed"})`);
    if (observed.inView !== "shown") failures.push(`the article ${cites} the address names is ${observed.inView === "absent" ? "not on the page, first" : `not in view (${observed.inView ?? "not observed"})`}`);
    else if (observed.targetFocused !== true) failures.push(`the article ${cites} the address names is in view without the focus`);
    // The cited article carries its citations, and `verify` confirms its evidence form by both its hashes: the version's, in
    // the permalink it prints, and the text's (R65: "verify resolves every form").
    if (observed.citedEvidence !== undefined) {
      if (observed.citedEvidence.length === 0) failures.push(`the article ${cites} the address names carries no evidence form`);
      const checks = observed.evidenceVerifications ?? [];
      if (checks.length !== observed.citedEvidence.length) failures.push(`${observed.citedEvidence.length - checks.length} evidence forms were not verified`);
      for (const check of checks) {
        if (check.refusal !== null) failures.push(`verify refused the evidence form's permalink ${check.identifier} with ${check.refusal}`);
        else if (check.verdict !== "digest_matches") failures.push(`verify found the evidence form's permalink ${check.identifier} ${check.verdict}, not digest_matches`);
        else if (check.articleTextSha256 !== check.textSha256) {
          failures.push(`verify names the text digest ${check.articleTextSha256} at ${check.identifier}, and its evidence form carries ${check.textSha256}`);
        }
      }
    }
  }
  if (step.stale === true && expected.state !== "success" && expected.refusalCode === "pinned_digest_mismatch") {
    if (observed.pinNotHeld !== true) failures.push(`the page does not say that the link pins no version held at its date (${observed.pinNotHeld ?? "not observed"})`);
    else {
      if (observed.inView !== "shown") failures.push(`the notice that the link pins no version held at its date is not in view (${observed.inView ?? "not observed"})`);
      else if (observed.targetFocused !== true) failures.push("the notice that the link pins no version held at its date is in view without the focus");
      if (typeof observed.pinCurrent !== "string" || parseSitePath(observed.pinCurrent.replace(/^\/(?:en|fr)(?=\/)/, ""))?.kind !== "state") {
        failures.push(`the notice links no current version (${observed.pinCurrent ?? "none"})`);
      }
      // The notice is true only if the server says so too: `verify` refuses the permalink it names as no longer held.
      const said = observed.pinVerification?.refusal ?? observed.pinVerification?.verdict ?? "nothing";
      if (said !== "pinned_digest_mismatch") failures.push(`verify answers the permalink the notice names (${observed.pinNamed ?? "none"}) with ${said}, not pinned_digest_mismatch`);
    }
  }
  // A law's page names as canonical the address it shows in the page's own interface language, a page that answers itself
  // (the plan's item 3.6): its own address, article included, when it shows what that names, or at a permalink of the version
  // its act's readable address shows today, that readable address (R67: "identical version pages point their canonical link
  // there"); a stale permalink's page the version it shows, with no article and never the digest it was opened at (review of
  // #967: the canonical is what the answer shows, never the request). Each interface language is an alternate, and the
  // address with no interface language for any other, each named once: the links are counted, so a page that names one
  // twice fails.
  if ((expected.state === "success" || (step.stale === true && expected.refusalCode === "pinned_digest_mismatch")) && step.rendered === true && observed.canonical !== undefined) {
    const own = step.path.replace(/^\/(?:en|fr)(?=\/)/, "");
    const locale = /^\/(en|fr)\//.exec(step.path)?.[1] ?? null;
    const page = `${SITE_ORIGIN}/${locale}`;
    const named = typeof observed.canonical === "string" && locale !== null && observed.canonical.startsWith(`${page}/`) ? observed.canonical.slice(page.length) : null;
    // A page of a reading after its first names itself with its page (`?page=N`; the plan's item 3.7), and nothing else.
    const law = named === null || !/^[^?]*(?:\?page=[1-9][0-9]*)?$/.test(named) ? null : parseSitePath(named.split("?")[0]);
    const query = own.includes("?") ? own.slice(own.indexOf("?")) : "";
    const pinned = parseSitePath(own.split("?")[0]);
    const readable = pinned?.kind === "state"
      ? `${currentAddress({ publisher: pinned.publisher, work: pinned.publisher === "lu-legilux" ? pinned.identifier.slice("/lu-legilux/".length) : pinned.identifier, language: pinned.language, publisherId: anchorText(pinned.anchor) })}${query}`
      : null;
    if (law === null || law.locale !== null) failures.push(`the page names ${observed.canonical ?? "no"} canonical link, not a law's address on ${page}, the page's own interface language`);
    else if (step.stale === true ? (law.kind !== "state" || law.anchor !== null || named.includes(pinned?.digest ?? "")) : (named !== own && named !== readable)) {
      failures.push(`the page names ${observed.canonical} canonical, not ${step.stale === true ? "the version it shows" : `${page}${own}${readable === null ? "" : ` or ${page}${readable}`}`}`);
    } else {
      const wanted = [...INTERFACE_LANGUAGES.map((language) => [language, `${SITE_ORIGIN}/${language}${named}`]), ["x-default", `${SITE_ORIGIN}${named}`]];
      const seen = Array.isArray(observed.alternates) ? observed.alternates : [];
      const counted = (pairs) => JSON.stringify(pairs.map((pair) => JSON.stringify(pair)).sort());
      if (counted(seen) !== counted(wanted)) failures.push(`the page's alternates are ${JSON.stringify(observed.alternates ?? null)}, not ${JSON.stringify(wanted)}, each once`);
    }
  }
  if (expected.state === "success" && step.then !== undefined && observed.then !== "reached") {
    failures.push(`the page never showed ${step.then.until} once ${step.then.click} was clicked (${observed.then ?? "not clicked"})`);
  }
  for (const text of expected.texts ?? []) {
    if (!observed.text.includes(text)) failures.push(`the page does not show "${text}"`);
  }
  const ends = endAddressesOf(step, todaysOf(observed));
  if (observed.location !== undefined && !ends.some((end) => observed.location === `${expected.origin}${end}`)) {
    failures.push(`the page ended at ${observed.location}, not at ${ends.map((end) => `${expected.origin}${end}`).join(" or ")}`);
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
  if (observed.reflow !== undefined) {
    // Fails closed: a measurement that was not taken, or not taken at the reflow width, is no evidence the page fits.
    if (observed.reflow?.failed === true || typeof observed.reflow?.width !== "number" || typeof observed.reflow?.scrollWidth !== "number") {
      failures.push(`the page was not measured at ${REFLOW_WIDTH} CSS pixels`);
    } else if (observed.reflow.width > REFLOW_WIDTH) {
      failures.push(`the page was measured in a ${observed.reflow.width} px window, not one of at most ${REFLOW_WIDTH}`);
    } else if (observed.reflow.scrollWidth > observed.reflow.width) {
      failures.push(`at ${REFLOW_WIDTH} CSS pixels the page scrolls sideways (${observed.reflow.scrollWidth} px of content in a ${observed.reflow.width} px window): ${observed.reflow.offenders.join(", ") || "no single element"}`);
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
    failures.push(...processRecordFailures(observed.api, { typed: step.typed === undefined ? [] : [step.typed].flat(), userAgent: observed.userAgent }));
  }
  // Kept out of search engines, as every release is until the owner answers O27 (R67; `search-engines.mjs`): the page names
  // one robots element, noindex, and served by the API it arrives with X-Robots-Tag: noindex. A run against a release opened
  // to search engines says so (`searchEngines: "indexed"`): what each of its pages says then hangs on its indexing record,
  // which the servers' own tests hold.
  const keptOut = expected.searchEngines !== INDEXED;
  if (keptOut && observed.robots !== undefined && JSON.stringify(observed.robots) !== JSON.stringify(["noindex"])) {
    failures.push(`the page names the robots elements ${JSON.stringify(observed.robots)}, not one noindex: it is not kept out of search engines`);
  }
  if (expected.servedByApi) {
    const header = (name) => Object.entries(observed.pageHeaders ?? {}).find(([key]) => key.toLowerCase() === name)?.[1] ?? null;
    if (keptOut && header("x-robots-tag") !== X_ROBOTS_TAG) failures.push(`the page arrived with X-Robots-Tag ${header("x-robots-tag")}, not ${X_ROBOTS_TAG}`);
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

  // A page at a law's address the server renders from its own answer (R67) asks the API nothing as it loads and shows
  // its answer with no script run; any other page asks exactly once.
  if (expected.mustRender === true) {
    if (observed.serverRendered !== true) failures.push("the page at a law's address arrived without the answer the server renders it from");
    const without = observed.withoutScripts?.answerState ?? null;
    if (without !== expected.state) failures.push(`without scripts the page shows ${without === null ? "no answer" : `a ${without}`}, not a ${expected.state}`);
  }
  // The home page (R47, R11, R13): rendered by the server from its coverage answer, with the coverage line and today in
  // its date field, in its own bytes; and at a small phone's size the box first, and it and the sentence under it in view.
  if (step.frontDoor === true) failures.push(...frontDoorFailures(observed, expected));
  // A page request nothing claims is the site's 404 page, with status 404, in both servers (R60).
  if (step.notFound === true && observed.pageStatus !== 404) {
    failures.push(`the page at ${step.path} arrived with status ${observed.pageStatus ?? "not observed"}, not 404`);
  }
  // A step that retries a failure presses the failure panel's retry once, which asks the very same request once more (R60).
  if (step.retry === true && observed.retried !== "reached") {
    failures.push(`the failure panel's retry was not pressed and settled (${observed.retried ?? "not observed"})`);
  }
  const toApi = observed.requests.filter((request) => new URL(request.url).pathname.startsWith("/api/"));
  const asks = (expected.mustRender === true ? 0 : 1) + (step.retry === true ? 1 : 0) + (step.then?.asks ?? 0);
  if (toApi.length !== asks) {
    failures.push(`the page made ${toApi.length} requests to the API, not exactly ${["none", "one", "two"][asks]}`);
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
      // A home page's body names today, which is the run's today at its start or at its end (a run across midnight in
      // Luxembourg meets either).
      const wanted = [...new Set(todaysOf(observed).map((today) => JSON.stringify(bodyOn(step.body, today))))];
      if (!wanted.includes(JSON.stringify(body))) {
        failures.push(`the API request's body was ${request.postData ?? "not observed"}, not ${wanted.join(" or ")}`);
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
  // Once the page has loaded, its language menu leads to this same page in each interface language, the article named
  // after # included (R61; review of #958: nothing held the rewrite), as the page's address stands at the end: the search it
  // keeps after "#" and the date a readable address keeps (`sameAddressIn`; the plan's items 3.4 and 3.5).
  if (observed.languageLinks !== undefined) {
    const here = new URL(observed.location);
    for (const language of INTERFACE_LANGUAGES) {
      const expectedHref = sameAddressIn(language, { pathname: here.pathname, search: here.search, hash: here.hash });
      const href = observed.languageLinks?.[language] ?? null;
      if (href !== expectedHref) failures.push(`the language menu's ${language} link leads to ${href ?? "nothing"}, not ${expectedHref}`);
    }
  }
  if (observed.storage?.local !== 0 || observed.storage?.session !== 0) {
    failures.push(Number.isInteger(observed.storage?.local) && Number.isInteger(observed.storage?.session) ? "the page wrote to storage" : "the page's storage could not be read");
  }
  if (observed.csp !== cspValue()) failures.push("the page's CSP is not the reviewed one");
  if (observed.hydrated !== "clean") failures.push(`hydration was ${observed.hydrated ?? "never reported"}, not clean`);
  return failures;
}

/**
 * What a server process recorded while it served a run, as failures (empty when it recorded nothing): the launch contract's
 * "no query text, IP or user agent recorded". `record` is `{ startup, output, changedFiles, fileEvents }`: what it wrote
 * before it first answered, what it wrote after, the files changed under its directory and the watch's events on its
 * directory and temporary folder (the container's /tmp for the image). `typed` are the texts the run sent, `userAgent`
 * the browser's when a browser ran. A browser run holds its page's server to it (`journeyVerdict`), and the image's probes
 * without a browser the one start they all ask (`image-rehearsal.mjs`, `probeImage`).
 */
export function processRecordFailures(record, { typed = [], userAgent = null } = {}) {
  const failures = [];
  const everything = `${record.startup ?? ""}${record.output}`;
  for (const text of typed) {
    if (everything.includes(text)) failures.push(`the API process recorded the query text "${text}"`);
  }
  if (userAgent && everything.includes(userAgent)) failures.push("the API process recorded the browser's user agent");
  if (record.output.includes("127.0.0.1")) failures.push("the API process recorded an address");
  if (record.output.trim() !== "") failures.push(`the API process wrote output during the run: ${record.output.trim().slice(0, 200)}`);
  if (record.changedFiles.length > 0) failures.push(`the API process wrote files: ${record.changedFiles.join(", ")}`);
  const fileEvents = record.fileEvents ?? [];
  const watchErrors = fileEvents.filter(([event]) => event === "error").map(([, message]) => message);
  if (watchErrors.length > 0) failures.push(`the file watch failed, so the run cannot say the API touched no file: ${watchErrors.join("; ")}`);
  const touched = [...new Set(fileEvents.filter(([event]) => event !== "error").map(([, path]) => path))];
  if (touched.length > 0) failures.push(`the API process touched files while serving the run: ${touched.join(", ")}`);
  return failures;
}

/** The days a run was in, in Luxembourg: at its start and at its end (once when they are one), or now for a record with none. */
function todaysOf(observed) {
  return Array.isArray(observed.todays) && observed.todays.length > 0 ? observed.todays : [todayInLuxembourgAt(Date.now())];
}

/** What a home page's own bytes say of its front door: its coverage line's state, its date field's value, its facts block. */
export function frontDoorOf(bytes) {
  const field = /<input[^>]*data-search-date=""[^>]*>/.exec(bytes)?.[0] ?? null;
  return {
    coverageLine: /data-coverage-line="([a-z]+)"/.exec(bytes)?.[1] ?? null,
    dateField: field === null ? null : (/\svalue="([^"]*)"/.exec(field)?.[1] ?? ""),
    facts: bytes.includes('id="lex-home-facts"'),
  };
}

/**
 * The home page's own checks, as failures: rendered by the server (its facts block in the bytes it was sent), its
 * coverage line counted (or, with no mount, the no-mount sentence), its date field holding the run's today in Luxembourg;
 * and at 320 by 640 CSS pixels the box the first control of the content, and it and the sentence under it in view.
 */
export function frontDoorFailures(observed, expected) {
  const failures = [];
  const door = observed.frontDoor;
  if (door === undefined) return ["the home page's own bytes were not read"];
  if (door.facts !== true) failures.push("the home page arrived without the facts the server renders it from");
  const unmounted = expected.state === "refusal" && expected.refusalCode === "no_corpus_mounted";
  const line = unmounted ? "unmounted" : "counted";
  if (door.coverageLine !== line) failures.push(`the home page's coverage line is ${door.coverageLine ?? "absent"}, not ${line}`);
  if (!todaysOf(observed).includes(door.dateField)) {
    failures.push(`the home page's date field holds ${JSON.stringify(door.dateField)}, not today in Luxembourg (${todaysOf(observed).join(" or ")})`);
  }
  const screen = observed.firstScreen;
  if (screen === undefined || screen === null || typeof screen.height !== "number") failures.push("the home page was not measured at a small phone's size");
  else {
    if (screen.inputFirst !== true) failures.push("the search box is not the first control of the home page's content");
    if (!(typeof screen.input === "number" && screen.input <= screen.height)) failures.push(`the search box ends at ${screen.input} px, below the first ${screen.height} px`);
    if (!(typeof screen.statement === "number" && screen.statement <= screen.height)) failures.push(`the sentence under the box ends at ${screen.statement} px, below the first ${screen.height} px`);
  }
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

/**
 * `watchFiles` over the API's directory and its temporary folder at once: one `stop()` answering both folders' events,
 * the temporary folder's names prefixed with `TMP/`. The image's probe watches the container's /tmp; a run here watched
 * only the API's directory, so the SQLite temporary files the API wrote while serving the population mount were seen
 * only by the release (2026-10-13).
 */
export function watchApiFiles(home, temporary) {
  const watches = [[watchFiles(home), ""], [watchFiles(temporary), "TMP/"]];
  let stopped = null;
  return {
    stop() {
      stopped ??= Promise.all(watches.map(async ([watched, prefix]) =>
        (await watched.stop()).map(([event, name]) => [event, event === "error" ? name : `${prefix}${name}`])))
        .then((events) => events.flat());
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

/**
 * How long an API is given to answer after it starts: a minute, and a minute more per GiB of the indexes it verifies into
 * its temporary folder before it listens (the combined population mount's 2.3 GB took about 50 s on this machine, more
 * than the minute a fixture mount needed, 2026-10-13).
 */
export async function startDeadlineMs(mount) {
  const indexMebibytes = mount ? (await tmpMebibytesFor(mount)) - TMP_MARGIN_MEBIBYTES : 0;
  return 60_000 + Math.ceil((indexMebibytes / 1024) * 60_000);
}

export async function startApi(apiOutput, mount, webRoot = null) {
  const home = await mkdtemp(join(tmpdir(), "lex-journey-api-"));
  // The API's own temporary folder, beside its directory and removed with it: mounting copies each index there, and a run
  // that ends the process would otherwise leave the copies (2.3 GB a start for the population mount).
  const temporary = await mkdtemp(join(tmpdir(), "lex-journey-tmp-"));
  await cp(apiOutput, home, { recursive: true });
  await rm(join(home, "v3-corpus"), { recursive: true, force: true });
  await rm(join(home, "v3-web"), { recursive: true, force: true });
  if (mount) await cp(mount, join(home, "v3-corpus"), { recursive: true });
  if (webRoot) await cp(webRoot, join(home, "v3-web"), { recursive: true });
  const port = await freePort();
  const origin = `http://127.0.0.1:${port}`;
  const child = spawn("dotnet", [join(home, "Lex.V3.Api.dll")], {
    cwd: home,
    env: { ...process.env, ASPNETCORE_URLS: origin, DOTNET_NOLOGO: "1", TMP: temporary, TEMP: temporary, TMPDIR: temporary },
    stdio: ["ignore", "pipe", "pipe"],
  });
  // Both outputs, from the first byte: what the process records is measured, not assumed.
  let stderr = "";
  let output = "";
  child.stdout.on("data", (chunk) => { output += chunk; });
  child.stderr.on("data", (chunk) => { stderr += chunk; output += chunk; });
  let exited = false;
  child.on("exit", () => { exited = true; });
  const allowedMs = await startDeadlineMs(mount);
  const deadline = Date.now() + allowedMs;
  while (Date.now() < deadline && !exited) {
    try {
      const answer = await fetch(`${origin}/api/v3/coverage`, {
        method: "POST", headers: { "content-type": "application/json" }, body: '{"operation_id":"coverage","parameters":{}}',
      });
      if (answer.status === 200) {
        const filesAtStart = await listFiles(home);
        const fileWatch = watchApiFiles(home, temporary);
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
            await rm(temporary, { recursive: true, force: true }).catch(() => {});
          },
        };
      }
    } catch {
      // Not listening yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  // Read before the process is ended: ending it is an exit too, and reported a timeout as one (2026-10-13).
  const exitedByItself = exited;
  child.kill();
  await new Promise((resolve) => setTimeout(resolve, 500));
  await rm(home, { recursive: true, force: true }).catch(() => {});
  await rm(temporary, { recursive: true, force: true }).catch(() => {});
  throw new Error(exitedByItself
    ? `the API exited before it answered: ${stderr}`
    : `the API did not answer within ${allowedMs / 1000} s: ${stderr}`);
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
 * The keyboard path to one control (a step's `then.key`): from where the focus is, Tab until the element matching
 * `selector` has the focus, recording each stop, then press `key` on it. "absent" when Tab never reaches it.
 */
async function keyboardActivate(session, sessionId, evaluate, stops, selector, key) {
  for (let tab = 0; tab < 200; tab += 1) {
    await pressKey(session, sessionId, "Tab");
    const focused = await evaluate(FOCUSED);
    if (focused === null) continue;
    stops.push(focused);
    if (await evaluate(`document.activeElement?.matches(${JSON.stringify(selector)}) === true`)) {
      await pressKey(session, sessionId, key);
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

/**
 * Reflow (WCAG 2.2 success criterion 1.4.10, the launch contract's accessibility line): the page laid out at 320 CSS
 * pixels, a small phone's width and 1280 pixels at 400 per cent zoom, must not scroll sideways, with every disclosure in
 * the answer open, so that what one click shows is measured too (review of #957: the export's JSON, opened, made the page
 * 9,518 pixels wide). A table may scroll inside its own region (two-dimensional content is excepted), so an element
 * inside a box that scrolls or clips sideways is no offender. `width` is the window's, `scrollWidth` the document's,
 * `offenders` what stands out past the window or overflows its own box.
 */
export const REFLOW_WIDTH = 320;
const REFLOW = `(() => {
  const width = document.documentElement.clientWidth;
  const scrollWidth = document.documentElement.scrollWidth;
  const contained = (el) => {
    for (let node = el.parentElement; node !== null && node !== document.body; node = node.parentElement) {
      if (['auto', 'scroll', 'hidden', 'clip'].includes(getComputedStyle(node).overflowX)) return true;
    }
    return false;
  };
  const named = (el) => el.tagName.toLowerCase() + (typeof el.className === 'string' && el.className ? '.' + el.className.split(' ')[0] : '')
    + (el.closest('[data-live-answer]') ? ' in the answer' : '');
  const offenders = scrollWidth <= width ? [] : [...document.querySelectorAll('body *')].flatMap((el) => {
    if (contained(el)) return [];
    // An element standing out past the window, or one whose own content runs past its box (a line that does not wrap).
    if (el.getBoundingClientRect().right > width + 1) return [named(el) + ' ' + Math.round(el.getBoundingClientRect().width) + ' px'];
    const style = getComputedStyle(el);
    if (el.scrollWidth > el.clientWidth + 1 && style.overflowX === 'visible' && el.clientWidth > 0) return [named(el) + ' overflowing its box to ' + el.scrollWidth + ' px'];
    return [];
  });
  return { width, scrollWidth, offenders: [...new Set(offenders)].slice(0, 8) };
})()`;

/**
 * The home page's first screen (R47: on phones the input stays first, and the one-line statement stays visible), at the
 * top of the page: whether the search box is the first control of the page's content, and where it and the sentence under
 * it end, against the window's height.
 */
const FIRST_SCREEN = `(() => {
  window.scrollTo(0, 0);
  const main = document.querySelector('main');
  const first = main?.querySelector('input, select, textarea, button') ?? null;
  const box = main?.querySelector('form[role=search] input[type=search]') ?? null;
  const statement = main?.querySelector('[data-not-advice]') ?? null;
  const bottom = (el) => (el === null ? null : Math.round(el.getBoundingClientRect().bottom));
  return { height: window.innerHeight, inputFirst: first !== null && first === box, input: bottom(box), statement: bottom(statement) };
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
    let pageStatus = null;
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
        pageStatus = message.params.statusCode ?? null;
      } else if (message.method === "Runtime.consoleAPICalled") {
        consoleMessages.push(message.params.args.map((arg) => arg.value ?? arg.description ?? "").join(" "));
      } else if (message.method === "Log.entryAdded" && step.notFound === true && message.params.entry.source === "network"
        && message.params.entry.url === `${pageOrigin}${step.path}` && / 404 /.test(message.params.entry.text)) {
        // The browser's own note that the document it was sent is a 404 is what a 404 page is: its status is checked instead.
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
    const todayAtStart = todayInLuxembourgAt(Date.now());
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
      if (keyboard && step.then.key !== undefined) {
        // A link or button the step acts on by the keyboard (the next articles, the plan's item 3.7): Tab until it has the
        // focus, then its key, as a keyboard user follows it.
        then = await keyboardActivate(session, sessionId, evaluate, keys?.stops ?? [], step.then.click, step.then.key);
      } else if (keyboard) {
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
    // A step that retries a failure: once the page shows the failure panel, its retry is pressed (by the keyboard on a
    // keyboard run: Tab until it has focus, then Enter), and the page must ask again and settle.
    let retried;
    if (step.retry === true) {
      const asked = () => requests.filter((request) => new URL(request.url).pathname.startsWith("/api/")).length;
      const before = asked();
      if (!(await evaluate("document.querySelector('[data-live-answer] [data-failure] [data-retry]') !== null"))) retried = "absent";
      else if (keyboard) {
        retried = "absent";
        for (let tab = 0; tab < 30; tab += 1) {
          await pressKey(session, sessionId, "Tab");
          const focused = await evaluate(FOCUSED);
          if (focused === null) continue;
          keys?.stops.push(focused);
          if (await evaluate("document.activeElement?.hasAttribute('data-retry') === true")) {
            await pressKey(session, sessionId, "Enter");
            retried = "pressed";
            break;
          }
        }
      } else {
        await evaluate("document.querySelector('[data-live-answer] [data-failure] [data-retry]').click()");
        retried = "pressed";
      }
      while (retried === "pressed" && Date.now() < deadline) {
        const state = await evaluate("document.querySelector('[data-answer-state]')?.dataset.answerState ?? null");
        if (asked() > before && state !== null && state !== "loading") retried = "reached";
        else await new Promise((resolve) => setTimeout(resolve, 100));
      }
      answerState = await evaluate("document.querySelector('[data-answer-state]')?.dataset.answerState ?? null");
    }
    // Let a late request or log line arrive before the observation is closed.
    await new Promise((resolve) => setTimeout(resolve, 500));
    const observed = {
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
      // A document with no storage of its own (the browser's error page) throws on reading it: that is no storage read.
      storage: await evaluate("(() => { try { return { local: localStorage.length, session: sessionStorage.length }; } catch { return { local: null, session: null }; } })()"),
      csp: await evaluate("document.querySelector('meta[http-equiv=\"Content-Security-Policy\"]')?.content ?? null"),
      hydrated: await evaluate("document.documentElement.dataset.hydrated ?? null"),
      location: await evaluate("location.href"),
      history: {
        atLoad: historyAtLoad,
        atEnd: await evaluate("history.length"),
        state: await evaluate("JSON.stringify(history.state)"),
      },
      cookie: await evaluate("document.cookie"),
      // Where each reviewed language's link in the language menu leads, once the page has loaded.
      languageLinks: await evaluate("Object.fromEntries([...document.querySelectorAll('[data-locale-nav] a[data-same-document]')].map((link) => [link.dataset.sameDocument, link.getAttribute('href')]))"),
      pageHeaders,
      pageStatus,
      retried,
      then,
      userAgent: await evaluate("navigator.userAgent"),
      liveRegion: { atLoad: liveRegionAtLoad, atEnd: await evaluate(ANSWER_LIVE_REGION) },
      keyboard: keys,
      // Every citation the answer prints, as code or as a link (`answerCitations`). The permalink a stale link's notice
      // names is no citation: the notice says it is not the version served, or held; the current version it links is one.
      citations: answerCitations(
        await evaluate("[...document.querySelectorAll('[data-live-answer] code')].filter((node) => node.closest('[data-pin-replaced], [data-pin-not-held]') === null).map((node) => node.textContent.trim())"),
        await evaluate("[...document.querySelectorAll('[data-live-answer] a[href]')].map((node) => node.getAttribute('href'))"),
      ),
      pinNamed: await evaluate("document.querySelector('[data-live-answer] [data-pin-replaced] code, [data-live-answer] [data-pin-not-held] > code')?.textContent.trim() ?? null"),
      // The cited article's evidence form (`Citations`; R65): the permalink it prints and the text digest it carries, which
      // `verify` must both confirm once the run is recorded (`verifyEvidence`).
      citedEvidence: await evaluate("[...document.querySelectorAll('[data-live-answer] [data-citation-form=evidence]')].map((node) => ({ permalink: node.dataset.permalink ?? null, textSha256: node.dataset.textSha256 || null }))"),
      // At a stale permalink: whether the page says the link pins no version held at its date, and the current link it gives.
      pinNotHeld: step.stale === true ? await evaluate("document.querySelector('[data-pin-not-held]') !== null") : undefined,
      pinCurrent: step.stale === true ? await evaluate("document.querySelector('[data-pin-not-held] a[href]')?.getAttribute('href') ?? null") : undefined,
      // Whether the document arrived carrying the answer the server rendered it from (`initial-answer.mjs`).
      serverRendered: await evaluate("document.getElementById('lex-initial-answer') !== null"),
      // What the page says to search engines: its robots elements' contents, every one (R67).
      robots: await evaluate("[...document.querySelectorAll('meta[name=\"robots\"]')].map((meta) => meta.getAttribute('content'))"),
      // At a law's address: the canonical link and the alternates the page's head names once loaded (R64, R67), every one
      // of them (review of #967): two canonical links are observed as the list of both, and each alternate as a pair.
      ...(step.rendered === true ? {
        canonical: await evaluate("(() => { const links = [...document.head.querySelectorAll('link[rel=\"canonical\"]')].map((link) => link.getAttribute('href')); return links.length > 1 ? links : links[0] ?? null; })()"),
        alternates: await evaluate("[...document.head.querySelectorAll('link[rel=\"alternate\"][hreflang]')].map((link) => [link.getAttribute('hreflang'), link.getAttribute('href')])"),
      } : {}),
      // At a permalink: whether the page says another version stands in for the pinned one, and where the article it
      // names is (`shown`: its top inside the window).
      pinReplaced: step.pins === undefined && step.stale !== true ? undefined : await evaluate("document.querySelector('[data-pin-replaced]') !== null"),
      // At a permalink or a readable address: where what the address names is (`shown`: its top inside the window) and
      // whether it has the focus, as an anchor a browser follows would: the cited article, first (R64), which the page
      // focuses with what it says first (`data-pinned-lead`), or for a stale permalink the notice.
      ...(step.pins === undefined && step.current === undefined && step.stale !== true ? {} : await evaluate(`(() => {
        const answer = document.querySelector('[data-live-answer]');
        const node = ${step.stale === true
          ? "answer?.querySelector('[data-pin-not-held], [data-pin-replaced]') ?? undefined"
          : `[...(answer?.querySelectorAll('[data-cited-article]') ?? [])].find((candidate) => candidate.dataset.citedArticle === ${JSON.stringify(step.pins?.anchor ?? step.current?.anchor ?? "")})`};
        if (node === undefined) return { inView: "absent", targetFocused: false };
        const top = node.getBoundingClientRect().top;
        const focused = node === document.activeElement || node.closest('[data-pinned-lead]') === document.activeElement;
        // A fraction of a pixel above the window's top is the top (scrolling aligns to a fraction on some fonts: CI's Linux).
        return { inView: top > -1 && top < innerHeight ? "shown" : "at " + top.toFixed(1) + " px of a " + innerHeight + " px window", targetFocused: focused };
      })()`)),
      // Every quotation the answer shows, with what its article's element prints beside it.
      paint: await evaluate(PAINT_ONLY),
      emptyAnswer: await evaluate("document.querySelector('[data-live-answer] [data-no-row], [data-live-answer] [data-no-hit]') !== null"),
      quotes: await evaluate("[...document.querySelectorAll('[data-live-answer] [data-article]')].filter((node) => node.querySelector('blockquote') !== null).map((node) => ({ article: node.dataset.article, codes: [...node.querySelectorAll('code')].map((code) => code.textContent.trim()) }))"),
    };
    // Reflow, measured last so that nothing above is read at this width: the page as it ended, laid out again at
    // `REFLOW_WIDTH` (a desktop window that narrow, so that the layout viewport cannot grow to fit the content as a
    // phone's does).
    await evaluate("document.querySelectorAll('[data-live-answer] details').forEach((node) => { node.open = true; })");
    await session.send("Emulation.setDeviceMetricsOverride", { width: REFLOW_WIDTH, height: 640, deviceScaleFactor: 1, mobile: false }, sessionId);
    await new Promise((resolve) => setTimeout(resolve, 300));
    // A measurement that could not be taken fails the run (`journeyVerdict`), never passes it.
    observed.reflow = (await evaluate(REFLOW)) ?? { failed: true };
    // The home page at a small phone's size (R47): the window is now 320 by 640.
    if (step.frontDoor === true) observed.firstScreen = await evaluate(FIRST_SCREEN);
    observed.todays = [...new Set([todayAtStart, todayInLuxembourgAt(Date.now())])];
    return observed;
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

/**
 * What a page is held to once the API has answered its request (`fromApi`): the API's outcome, then the step's own
 * refinement of it, given what the API answered (`realMountExpectations`), which may hold the page to more.
 */
export function expectedForAnswer(expected, envelope) {
  const derived = { ...expected, ...expectedFromEnvelope(envelope) };
  return derived.refine ? { ...derived, ...derived.refine(envelope) } : derived;
}

/** The request a step's page asks: its body, or coverage's, which the page asks as it loads. */
const COVERAGE_BODY = Object.freeze({ operation_id: "coverage", parameters: Object.freeze({}) });

/** Asks the API's `verify` for each citation, as a reader checking one would, and keeps what it said. */
async function verifyCitations(origin, citations) {
  const verifications = [];
  for (const identifier of citations) {
    const answer = await fetchWithinLimit(`${origin}/api/v3/verify`, {
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
 * Asks the API's `verify` for each evidence form a page printed (its permalink, as printed, under the site's origin), and
 * keeps what it said beside the text digest the form carries: the form is verified by both its hashes (R65).
 */
export async function verifyEvidence(origin, cited, fetchImpl = fetch) {
  const checks = [];
  for (const { permalink, textSha256 } of cited) {
    const answer = await fetchWithinLimit(`${origin}/api/v3/verify`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: "verify", parameters: { identifier: permalink } }),
    }, { fetchImpl });
    const envelope = await answer.json();
    const value = envelope.result?.value ?? null;
    checks.push({
      identifier: permalink,
      textSha256,
      refusal: envelope.refusal?.code ?? (value === null ? `HTTP ${answer.status}` : null),
      verdict: value?.verdict ?? null,
      articleTextSha256: value?.article_text_sha256 ?? null,
    });
  }
  return checks;
}

/**
 * One step: a server (the API from its build output, or whatever `expected.startServer` starts, such as
 * the release image), the page asked through the browser, and the verdict. A server answers
 * `{ origin, output(), outputAtStart, fileWatch, changedFiles(), close() }`.
 */
export async function run(apiOutput, mount, expected, browser, liveRoot) {
  const api = expected.startServer ? await expected.startServer() : await startApi(apiOutput, mount, expected.servedByApi ? liveRoot : null);
  // A step at a law's address is rendered by the server from its own answer (R67): the corpus's answer, or with no corpus
  // mounted the refusal every request gets; both servers render it.
  if (expected.step.rendered === true) expected = { ...expected, mustRender: true };
  // A step opened at a permalink opens the one the server's own dossier names (`pinnedStep`).
  if (expected.step.pins !== undefined) expected = { ...expected, step: await pinnedStep(api.origin, expected.step) };
  if (expected.fromApi) {
    // Asked before the browser, of the same server, with the page's own request.
    const body = bodyOn(expected.step.body ?? COVERAGE_BODY, todayInLuxembourgAt(Date.now()));
    const answer = await fetchWithinLimit(`${api.origin}/api/v3/${body.operation_id}`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
    expected = expectedForAnswer(expected, await answer.json());
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
    // What `verify` says of the permalink a notice names as not the version served.
    if (observed.pinNamed) observed.pinVerification = (await verifyCitations(api.origin, [observed.pinNamed]))[0];
    // And of the cited article's evidence form, by its permalink under the site's origin as printed.
    if (Array.isArray(observed.citedEvidence) && observed.citedEvidence.length > 0) observed.evidenceVerifications = await verifyEvidence(api.origin, observed.citedEvidence);
    // What a reader with no script receives: the page's own bytes, as the server sends them.
    if (expected.mustRender === true) {
      const bytes = await (await fetch(`${pageOrigin}${expected.step.path.split("#")[0]}`)).text();
      observed.withoutScripts = { answerState: /<section data-answer-state="([a-z_]+)"/.exec(bytes)?.[1] ?? null };
    }
    // The home page as a reader with no script receives it.
    if (expected.step.frontDoor === true) observed.frontDoor = frontDoorOf(await (await fetch(`${pageOrigin}${expected.step.path}`)).text());
    return { observed, failures: journeyVerdict(observed, { ...expected, origin: pageOrigin }) };
  } finally {
    if (live !== null) await new Promise((resolve) => live.close(resolve));
    await api.close();
  }
}

/**
 * The steps against a real mount (`--real-mount`): each page is held to what the API answers its
 * request, and to every invariant a run checks. The coverage page must name the mounted corpus and
 * Luxembourg index by the digests the mount's build report records. Journeys J1 (a date before the work's
 * history: on a real mount, a date before any held Luxembourg state), J2 (a question no article carries) and
 * J5 (a law the corpus does not hold) run as on the fixture mount. When the build report names an EU index,
 * the EU steps run too: the GDPR searched by its CELEX, its dossier and its reading, every EU citation
 * pinned and verified (`realMountSteps`).
 */
export function realMountSteps(report, held = null) {
  const steps = [
    ...Object.entries(held ? journeyStepsFor(held) : JOURNEY_STEPS),
    ["J1, a date before the history", held ? preHistoryStepFor(held.identifier, held.beforeHistory) : PRE_HISTORY_READING_STEP],
    ["J2, a question no article carries", NO_HIT_SEARCH_STEP],
    ["J5, a law not held", UNKNOWN_LAW_STEP],
  ];
  return report.europeIndex
    ? [...steps, ["eu search", EU_SEARCH_STEP], ["eu dossier", EU_DOSSIER_STEP], ["eu reading", EU_READING_STEP]]
    : steps;
}

/**
 * What a real mount's page is held to beyond the API's outcome, for the specification's journeys:
 * - J1: the date precedes every held state, so the reading must refuse `no_version_for_date`, saying no earlier
 *   state is held, whatever the API answered (another date's text would be the defect);
 * - J5: the law is not held, so the dossier must refuse `identifier_unknown` with the absence note, whatever the
 *   API answered (an answer would be the defect);
 * - J2: when the API finds no hit, the page must say that this is not evidence that the law does not exist; a
 *   population that does carry the words answers hits, held like any search.
 */
export function realMountExpectations(step) {
  if (step === PRE_HISTORY_READING_STEP || step.preHistory === true) return { refine: () => ({ state: "refusal", refusalCode: "no_version_for_date", texts: [NO_EARLIER_STATE] }) };
  if (step === UNKNOWN_LAW_STEP) return { refine: () => ({ state: "refusal", refusalCode: "identifier_unknown", texts: [ABSENCE_NOTE] }) };
  if (step === NO_HIT_SEARCH_STEP) return { refine: (envelope) => (expectedFromEnvelope(envelope).nothingToCite ? { texts: [ABSENCE_NOTE] } : {}) };
  return {};
}

/**
 * The Luxembourg work a real mount's steps ask for, read from the mount's own index (the fixture's work is not in a
 * population, so on the population mount its steps only ever met `identifier_unknown`, 2026-10-13): the first state,
 * in work key and date order, that is alone on its date and holds at least three articles with searchable text (the
 * export step pins three). Its date is the reading's, its first article id with text the history's anchor, and the day
 * before the work's first state J1's date. Null when the mount holds no Luxembourg index or no such state, and the
 * fixture's work is asked for, as before.
 */
export async function heldLuxembourgWork(mount) {
  const path = join(mount, "luxembourg-index.sqlite3");
  try {
    await stat(path);
  } catch {
    return null;
  }

  const { DatabaseSync } = await import("node:sqlite");
  const index = new DatabaseSync(path, { readOnly: true });
  try {
    const textOf = "FROM json_each(s.article_identities_json) j JOIN articles a ON a.article_identity_sha256 = j.value AND a.language = s.language WHERE a.searchable_text <> ''";
    const state = index.prepare(`
      SELECT s.work_key AS work, s.applicability_date AS date, s.language AS language,
             (SELECT min(t.applicability_date) FROM states t WHERE t.work_key = s.work_key) AS first,
             (SELECT count(*) ${textOf}) AS withText,
             (SELECT min(a.publisher_id) ${textOf}) AS anchor
      FROM states s
      WHERE (SELECT count(*) FROM states t WHERE t.work_key = s.work_key AND t.applicability_date = s.applicability_date) = 1
      ORDER BY s.work_key, s.applicability_date, s.state_sha256`).all().find((row) => row.withText >= 3);
    if (!state) return null;
    return { identifier: `/lu-legilux/${state.work}`, date: state.date, anchor: state.anchor, language: state.language, beforeHistory: dayBefore(state.first) };
  } finally {
    index.close();
  }
}

/** One operation asked of a server as a page asks it (`POST /api/v3/{operation}`), and its envelope; null when it answers no 200. */
export async function askOperation(origin, operation, parameters) {
  const answer = await fetchWithinLimit(`${origin}/api/v3/${operation}`, {
    method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: operation, parameters }),
  });
  return answer.ok ? answer.json() : null;
}

/** The day before a civil date, as a civil date. */
function dayBefore(date) {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() - 1);
  return day.toISOString().slice(0, 10);
}

/**
 * `heldLuxembourgWork`'s choice made from a server's answers, for a server whose files are not at hand (a deployed
 * revision; the review of #950 found its probe asking for the fixture's work, which a population does not hold): a search
 * for "loi" lists hits in work key and date order, each naming its state; the first state with at least three of them
 * whose dossier shows it alone on its date is the reading's, the first of their article ids the history's anchor, and the
 * day before the dossier's first state J1's date. Null when none qualifies, and the fixture's work is asked for, as before.
 */
export async function heldLuxembourgWorkFromAnswers(origin, { ask = askOperation } = {}) {
  const search = await ask(origin, "search", { query: "loi", language: "fra" });
  const states = new Map();
  for (const hit of search?.result?.value?.hits ?? []) {
    if (!hit.state_sha256 || !hit.work_key || !hit.applicability_date || !hit.publisher_id) continue;
    if (!states.has(hit.state_sha256)) states.set(hit.state_sha256, { work: hit.work_key, date: hit.applicability_date, language: hit.language, anchors: new Set() });
    states.get(hit.state_sha256).anchors.add(hit.publisher_id);
  }

  for (const { work, date, language, anchors } of states.values()) {
    if (anchors.size < 3) continue;
    const identifier = `/lu-legilux/${work}`;
    const held = (await ask(origin, "dossier", { identifier }))?.result?.value?.states ?? [];
    if (held.filter((state) => state.applicability_date === date).length !== 1) continue;
    const first = held.map((state) => state.applicability_date).sort()[0];
    return { identifier, date, anchor: [...anchors].sort()[0], language, beforeHistory: dayBefore(first) };
  }

  return null;
}

/** How many of the event log's events journey J8 verifies by permalink on a real mount: a population log holds thousands. */
export const REAL_MOUNT_EVENTS_VERIFIED = 25;

/** What a journey answered at the API alone stands as, among the browser runs: no page, no request, no citation. */
const API_ONLY_OBSERVED = Object.freeze({ answerState: "api", requests: [], console: [], hydrated: null, verifications: [], paint: null });

/**
 * The real mount's journeys: each step through the browser (`realMountSteps`), then journeys J6, J7 and J8 at the
 * API of the same kind of server (`apiJourneyRuns`), J8 verifying the first `REAL_MOUNT_EVENTS_VERIFIED` events'
 * permalinks. Returns `[label, { observed, failures }]` pairs; an API journey's `observed` is `API_ONLY_OBSERVED`.
 * `runStep` and `apiRuns` stand for `run` and `apiJourneyRuns`, so a test can see what each is asked.
 */
export async function realMountRuns(apiOutput, mount, options, browser, liveRoot, { runStep = run, apiRuns = apiJourneyRuns, held } = {}) {
  const report = JSON.parse(await readFile(join(mount, "build-report.json"), "utf8"));
  const work = held === undefined ? await heldLuxembourgWork(mount) : held;
  const runs = [];
  for (const [name, step] of realMountSteps(report, work)) {
    const digests = name === "coverage" ? { corpusSha256: report.corpus.Sha256, indexSha256: report.luxembourgIndex.Sha256 } : {};
    runs.push([`${name}, with the real mount`, await runStep(apiOutput, mount, { ...options, step, fromApi: true, ...digests, ...realMountExpectations(step) }, browser, liveRoot)]);
  }
  for (const [label, failures] of await apiRuns(apiOutput, mount, { startServer: options.startServer, eventsToVerify: REAL_MOUNT_EVENTS_VERIFIED })) {
    runs.push([`${label}, with the real mount`, { observed: API_ONLY_OBSERVED, failures }]);
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
    // A stale permalink pins no version held, so it is refused pinned_digest_mismatch before any rights are read (R64).
    const mustRefuse = name === "stale" ? { mustRefuse: "pinned_digest_mismatch" }
      : ["reading", "export", "permalink", "current"].includes(name) ? { mustRefuse: "text_withheld" } : {};
    const digests = name === "coverage" ? { corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 } : {};
    runs.push([`${name}, with the licence-blocked mount`, await run(apiOutput, mount, { ...options, step, fromApi: true, absentTexts, ...mustRefuse, ...digests }, browser, liveRoot)]);
  }
  return runs;
}

/**
 * What the dossier, the reading and Compare say of the fixture's one act, which is held only as first published (the
 * plan's item 0.6; Compare's since the review of #964): its label, as the English page sets it.
 */
export const AS_PUBLISHED_SAID = "As published; may have been amended since; no consolidated text.";

/**
 * A page request nothing claims (R60: "A 404 page offers search"): an address the site has no page for, under the English
 * interface. Both servers answer it with the 404 page, status 404, which offers the home page's search: the step searches
 * from it, as the home page's step does, and must end in that answer (or, with no mount, the refusal every request gets).
 */
export const NOT_FOUND_STEP = Object.freeze({ ...JOURNEY_STEPS.home, path: "/en/no-such-page", notFound: true, frontDoor: false });

/** What the 404 page says above its search: there is no page at the address. */
export const NOT_FOUND_SAID = "There is no page at this address";

/**
 * The search step with no mount, retried: the page shows the failure panel for `no_corpus_mounted`, the retry is pressed
 * once, and the page asks the very same request again and shows the same refusal (R60).
 */
export const RETRIED_SEARCH_STEP = Object.freeze({ ...JOURNEY_STEPS.search, retry: true });

/**
 * The search page opened at an address that keeps a search after "#" (R109; the plan's item 3.5), as a reader opens a link
 * someone shared: nothing typed, the page reads its fragment once hydrated and asks that search, the phrase in the request's
 * body alone, and stays at that address.
 */
export const KEPT_SEARCH_STEP = Object.freeze({
  path: `/en/search.html${searchFragment({ query: SEARCH_PHRASE, language: "fra" })}`,
  cites: true,
  operation: "search",
  body: JOURNEY_STEPS.search.body,
});

/** The runs of the site's own addresses on the fixture mount (the plan's item 3.5): a search kept in the address. */
export function addressExpectations() {
  const searchTexts = [SEARCH_FOUND_SAID, SEARCH_SEARCHED_SAID, SEARCH_VERSION_SAID, "art_15", "The first hits in the stated order, not the best hits."];
  return [["a search kept in the address", { step: KEPT_SEARCH_STEP, state: "success", texts: searchTexts }]];
}

/**
 * The runs of R60's pages on the fixture mount, as `[name, expected, mounted]` (`mounted`: with the fixture mount, else
 * with none): the 404 page searched, which answers as the home page does; the 404 page with no mount, whose search shows
 * the failure panel; and the search with no mount, its failure panel's retry pressed once.
 */
export function failurePageExpectations() {
  const searchTexts = [SEARCH_FOUND_SAID, SEARCH_SEARCHED_SAID, SEARCH_VERSION_SAID, "art_15", "The first hits in the stated order, not the best hits."];
  return [
    ["not found, with the fixture mount", { step: NOT_FOUND_STEP, state: "success", texts: [NOT_FOUND_SAID, ...searchTexts] }, true],
    ["not found, with no mount", { step: NOT_FOUND_STEP, state: "refusal", refusalCode: "no_corpus_mounted", texts: [NOT_FOUND_SAID, FAILURE_LINE] }, false],
    ["search retried, with no mount", { step: RETRIED_SEARCH_STEP, state: "refusal", refusalCode: "no_corpus_mounted", texts: [FAILURE_LINE] }, false],
  ];
}

/**
 * What each of the eight steps must show on the fixture mount (`journey-mount.json`), as `[step name,
 * expected]`: every page answers, with the texts the fixture's one work gives it.
 */
export function fixtureMountExpectations(journeyMount) {
  const { coverage, home, search, dossier, reading, history, compare, radar, export: exporting, address, permalink, stale, current } = JOURNEY_STEPS;
  const searchTexts = [SEARCH_FOUND_SAID, SEARCH_SEARCHED_SAID, SEARCH_VERSION_SAID, "art_15", "The first hits in the stated order, not the best hits.", NOT_ADVICE_SAID];
  // The home page's search is dated today, which the fixture's one state (2024-02-01) applies on; its coverage line counts the
  // fixture's one work, one state and 49 articles.
  const homeTexts = [
    ...searchTexts,
    "Only Luxembourg acts were searched. For each, the version whose publisher dates cover",
    "An act held only as published is searched as published, and may have been amended since.",
    "A search by words covers Luxembourg law. An ELI, a CELEX number, a Mémorial reference, a permalink, or the citation of an article of an act, with or without a date, opens that act, Luxembourg or EU, when this site holds it. To search within an EU act, name it by its CELEX number on the search page.",
    "This site holds 1 act of Luxembourg law, in 1 version with 49 articles, as of the index built on",
  ];
  return [
    ["coverage", { step: coverage, state: "success", corpusSha256: journeyMount.corpus_sha256, indexSha256: journeyMount.index_sha256 }],
    ["home", { step: home, state: "success", texts: homeTexts }],
    ["search", { step: search, state: "success", texts: [...searchTexts, "Only Luxembourg acts were searched, with no date: every version this site holds was searched"] }],
    ["dossier", { step: dossier, state: "success", texts: [journeyMount.work_key, "1 state, from 2024-02-01 to 2024-02-01.", "What this dossier does not hold", AS_PUBLISHED_SAID] }],
    ["reading", { step: reading, state: "success", texts: ["the state applying from 2024-02-01", "49 articles quoted", "Art. 15.", "Text served under agreed_same_run_cc_by", AS_PUBLISHED_SAID, NOT_ADVICE_SAID] }],
    ["history", { step: history, state: "success", texts: [`${HISTORY_ANCHOR} in loi-1991-08-10-n3`, "Carried by 1 held state, from 2024-02-01", "first held wording"] }],
    ["compare", { step: compare, state: "success", texts: [`loi-1991-08-10-n3: ${READING_DATE} against ${READING_DATE}.`, `Both dates select the one text held, the act as published, applicable from ${READING_DATE} (publisher); later versions are not held.`, AS_PUBLISHED_SAID] }],
    ["radar", { step: radar, state: "success", texts: [`${READING_DATE} to ${READING_DATE}: 1 state of 1 work, of 1 held.`, "not compared: the first state this index holds"] }],
    ["export", { step: exporting, state: "success", texts: ["3 articles pinned: 3 exported with text, 0 excluded.", EXPORT_WATERMARK, "Text served under agreed_same_run_cc_by."] }],
    ["address", { step: address, state: "success", texts: [journeyMount.work_key, "1 state, from 2024-02-01 to 2024-02-01."] }],
    ["permalink", { step: permalink, state: "success", texts: ["the state applying from 2024-02-01", "49 articles quoted", "Art. 15.", "Text served under agreed_same_run_cc_by", NOT_ADVICE_SAID] }],
    ["stale", { step: stale, state: "refusal", refusalCode: "pinned_digest_mismatch", texts: ["which is not the version this site holds for that date; the version it holds is"] }],
    ["current", { step: current, state: "success", texts: ["Quoted from loi-1991-08-10-n3, fra, the state applying from 2024-02-01.", "The whole act at this version", "49 articles quoted", NOT_ADVICE_SAID] }],
  ];
}

/** The line a failure panel says first (R60), as the English pages say it. */
export const FAILURE_LINE = "Lex could not complete this request. This is not an answer about the law.";

/** Journey J1's refusal: a reading asked for a date before the work's first held state. */
export const EARLY_READING_DATE = "2019-03-15";
export const EARLY_READING_STEP = Object.freeze({
  path: "/en/reading.html",
  cites: true,
  operation: "evidence_bundle",
  typed: Object.freeze([DOSSIER_IDENTIFIER, EARLY_READING_DATE]),
  body: Object.freeze({
    operation_id: "evidence_bundle",
    parameters: Object.freeze({ identifier: DOSSIER_IDENTIFIER, date: EARLY_READING_DATE }),
  }),
});

/** What a page says beside an absence it reports (J2's no hit, J5's law not held): absence in this build is not absence in law. */
export const ABSENCE_NOTE = "It is not evidence that the instrument or the law does not exist.";

/** What J1's refusal says: no state is held before the requested date. */
export const NO_EARLIER_STATE = "No earlier state is held: the requested date precedes this history.";

/**
 * Journey J1 on a real mount: the same work asked on a date before any Luxembourg state a population holds (the
 * fixture's date sits before its one state, but a population's history of the work may begin earlier).
 */
export const PRE_HISTORY_DATE = "1900-01-01";

/** Journey J1 for one work on one date before its history: the reading page, marked as J1 for its expectation. */
export function preHistoryStepFor(identifier, date) {
  return Object.freeze({
    ...EARLY_READING_STEP,
    preHistory: true,
    typed: Object.freeze([identifier, date]),
    body: Object.freeze({
      operation_id: "evidence_bundle",
      parameters: Object.freeze({ identifier, date }),
    }),
  });
}

export const PRE_HISTORY_READING_STEP = preHistoryStepFor(DOSSIER_IDENTIFIER, PRE_HISTORY_DATE);

/** Journey J2: a citizen's question searched as typed, which no held article carries. */
export const NO_HIT_PHRASE = "combien de jours de congé j'ai le droit quand mon père est décédé";
export const NO_HIT_SEARCH_STEP = Object.freeze({
  path: "/en/search.html",
  cites: true,
  operation: "search",
  typed: NO_HIT_PHRASE,
  body: Object.freeze({ operation_id: "search", parameters: Object.freeze({ query: NO_HIT_PHRASE, language: "fra", mode: "strict" }) }),
});

/** Journey J5: a law the corpus does not hold, asked for by its name. */
export const UNKNOWN_LAW = "Circulaire CSSF 20/747";
export const UNKNOWN_LAW_STEP = Object.freeze({
  path: "/en/dossier.html",
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
  const absence = ABSENCE_NOTE;
  return [
    ["J1, a date before the history", { step: EARLY_READING_STEP, state: "refusal", refusalCode: "no_version_for_date", texts: [NO_EARLIER_STATE, "2024-02-01"] }],
    // The plain no-hit card (the plan's item 4.5): what was searched, that only exact matching ran, the population searched and
    // the publishers' own search pages, above the absence note.
    ["J2, a question no article carries", { step: NO_HIT_SEARCH_STEP, state: "success", nothingToCite: true, texts: ["0 passages with the exact phrase, in 0 acts.", "What was searched", "Text: no article searched contains the phrase exactly as typed.", "Only exact matching ran on the text:", SEARCH_SEARCHED_SAID, "Legilux search, for Luxembourg law", "EUR-Lex advanced search, for EU law", absence] }],
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
  const today = todayInLuxembourgAt(Date.now());
  const bodies = [...Object.values(JOURNEY_STEPS), EARLY_READING_STEP, NO_HIT_SEARCH_STEP, UNKNOWN_LAW_STEP].map((step) => bodyOn(step.body ?? COVERAGE_BODY, today));
  return [...new Map(bodies.map((body) => [JSON.stringify(body), body])).values()];
}

async function postJson(url, body) {
  const answer = await fetchWithinLimit(url, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
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
 * request answers the same events (the log is append-only); the first page is numbered without a gap; from its own
 * `next_after` a log that said it has more answers the next events, starting where the first page ended and repeating
 * none, and one that said it has no more answers nothing more;
 * a cursor from another log is refused `snapshot_unknown`, never read as this log's; every event's permalink
 * verifies (the first `verifyAtMost` of the first page, on a real mount whose log holds thousands); and
 * `answer_drift` on a genesis log names no invalidated answer and asserts no absence of drift.
 */
export async function eventsFailures(origin, { verifyAtMost = Number.POSITIVE_INFINITY } = {}) {
  const failures = [];
  const ask = (operation, parameters) => postJson(`${origin}/api/v3/${operation}`, { operation_id: operation, parameters });
  const first = await ask("events", {});
  const value = first.json?.result?.value;
  if (first.json?.verdict !== "answer" || !Array.isArray(value?.events)) {
    failures.push(`events answered no event list: ${JSON.stringify(first.json).slice(0, 200)}`);
    return failures;
  }
  if (value.events.length === 0) failures.push("the mount's log holds no event to poll (a mount that holds no Luxembourg state cannot show journey J8)");
  const again = await ask("events", {});
  const repeated = firstDifference(withoutRequestFields(first.json), withoutRequestFields(again.json));
  if (repeated !== null) failures.push(`the same events request answered differently, first at ${repeated}`);
  const numbered = value.events.map((event) => event.seq);
  if (numbered.some((seq, index) => index > 0 && seq !== numbered[index - 1] + 1)) failures.push(`the first page's events are not numbered without a gap: ${numbered.slice(0, 10).join(",")}`);
  if (value.has_more === true) {
    const last = numbered.at(-1);
    if (!String(value.next_after).endsWith(`:${last}`)) failures.push(`next_after ${value.next_after} does not name the last event served (${last})`);
    const next = await ask("events", { after: value.next_after });
    const following = next.json?.result?.value?.events;
    if (!Array.isArray(following) || following.length === 0) failures.push(`from next_after a log that has more answered nothing: ${JSON.stringify(next.json).slice(0, 200)}`);
    else if (following[0].seq !== last + 1 || following.some((event) => event.seq <= last)) {
      failures.push(`the next page does not start where the first ended: ${following[0].seq} after ${last}`);
    }
  } else if (value.has_more === false) {
    const after = await ask("events", { after: value.next_after });
    const rest = after.json?.result?.value;
    if (!Array.isArray(rest?.events) || rest.events.length !== 0 || rest.has_more !== false) {
      failures.push(`from its own next_after the log answered more: ${JSON.stringify(after.json).slice(0, 200)}`);
    }
  }
  const foreign = await ask("events", { after: `${"0".repeat(64)}:1` });
  if (foreign.json?.refusal?.code !== "snapshot_unknown") failures.push(`a cursor from another log answered ${foreign.json?.refusal?.code ?? foreign.json?.verdict}, not snapshot_unknown`);
  for (const event of value.events.slice(0, verifyAtMost)) {
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
 * Journeys J6, J7 and J8 at the API, against one API process over the mount, or the server `startServer` starts
 * (the release image, a deployed revision): `ask` answered as the contained assistant's card; each page request,
 * and ask, events and answer drift, through REST and MCP; then the event log polled, verifying at most
 * `eventsToVerify` permalinks. Returns `[name, failures]` pairs.
 */
export async function apiJourneyRuns(apiOutput, mount, { startServer = null, eventsToVerify = Number.POSITIVE_INFINITY } = {}) {
  const api = startServer ? await startServer() : await startApi(apiOutput, mount);
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
    results.push(["J8, the event log polled and answer drift", await eventsFailures(api.origin, { verifyAtMost: eventsToVerify })]);
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
 * The long act's mount (`journey-mount.json` names `long_act`; the plan's item 3.7): the fixture act with 130 articles of
 * synthetic text, read in three pages of 50. Three steps, each on a page the server renders from its own answer: an article
 * on the second page, cited first, with the page that holds it, the act's contents and the links before and after; the
 * second page by the address the next-articles link names, as a reader without scripts follows it (the page shows it with
 * no script run); and the first page, whose next articles are asked in place by the link, by pointer or by the keyboard,
 * with one request at the next page and no navigation.
 */
export function longActExpectations(journeyMount) {
  const { date, language, permalink, cited, pages, articles } = journeyMount.long_act;
  const reading = (parameters) => Object.freeze({ operation_id: "evidence_bundle", parameters: Object.freeze({ identifier: permalink, date, language, ...parameters }) });
  const counted = `${articles} articles quoted`;
  const citedStep = Object.freeze({
    path: `/en${permalink}/${escapeSegment(cited)}`,
    rendered: true,
    pins: Object.freeze({ identifier: `/lu-legilux/${journeyMount.work_key}`, date, anchor: cited, language }),
    cites: true,
    operation: "evidence_bundle",
    body: reading({ article: cited }),
  });
  const secondPage = Object.freeze({
    path: `/en${permalink}?page=2`,
    rendered: true,
    cites: true,
    operation: "evidence_bundle",
    body: reading({ page: 2 }),
  });
  const inPlace = Object.freeze({
    path: `/en${permalink}`,
    rendered: true,
    cites: true,
    operation: "evidence_bundle",
    // The next articles, asked in place: one request at page 2, the articles added below, the pages shown 1 to 2.
    then: Object.freeze({ click: "[data-live-answer] a[data-next-page]", key: "Enter", until: "[data-pages-shown='1-2']", asks: 1 }),
    body: reading({ page: 2 }),
  });
  return [
    ["a long act's article on its second page, cited first", { step: citedStep, state: "success", texts: [`Page 2 of ${pages}: art_51 to art_100.`, "Contents", "Previous articles", "Next articles", counted, "Quoted from"] }],
    ["a long act's second page at the address its next-articles link names, without scripts", { step: secondPage, state: "success", texts: [`Page 2 of ${pages}: art_51 to art_100.`, "Previous articles", counted] }],
    ["a long act's next articles, asked in place", { step: inPlace, state: "success", texts: [`Pages 1 to 2 of ${pages}: art_1er to art_100.`, counted] }],
  ];
}

/**
 * The EU annex control mount (`journey-mount.json` names `eu_annex`): the reading, dossier and export composer pages for
 * the act whose annex the publisher's PDF holds only as images. Each page must say that the annex is not served as
 * text, and neither the annex's text nor its title may appear anywhere on the page, in its text or its markup (the JSON
 * the export shows included): the launch contract's annex line, walked in a browser. The export composer pins both
 * articles of the control case's wording, and must show the composed EU export with the annex listed as excluded, the
 * watermark, Decision 95's acknowledgement and the PDF offered.
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
  const exporting = Object.freeze({
    ...JOURNEY_STEPS.export,
    typed: Object.freeze([celex, date]),
    // The control case's wording quotes two articles: both are pinned, and the export must be composed with its annex.
    then: Object.freeze({ click: "input[data-pin]", count: 2, until: "[data-export-state=composed] [data-export-annexes]" }),
    body: Object.freeze({ operation_id: "evidence_bundle", parameters: Object.freeze({ identifier: celex, date }) }),
  });
  const line = annexes === 1
    ? "1 annex of the English wording is not served as text, and is never searched, quoted or exported"
    : `${annexes} annexes of the English wording are not served as text, and are never searched, quoted or exported`;
  const exported = [line, "2 articles pinned: 2 exported with text, 0 excluded.", EXPORT_WATERMARK, EUROPE_TEXT_ACKNOWLEDGEMENT, "Save as PDF"];
  return [
    ["the annex control case read on its wording date", { step: reading, state: "success", texts: [line], absentTexts }],
    ["the annex control case's dossier", { step: dossier, state: "success", texts: [line], absentTexts }],
    ["the annex control case exported", { step: exporting, state: "success", texts: exported, absentTexts }],
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
  // One that names an EU annex is the image-only annex control mount, and one that names a long act the reading by page's.
  const europeAnnex = journeyMount?.eu_annex !== undefined;
  const longAct = journeyMount?.long_act !== undefined;
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
  } else if (longAct) {
    for (const [name, expected] of longActExpectations(journeyMount)) {
      results.push([`${name}, with the long act mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
    }
  } else {
    // Each step with the fixture mount, then with no mount, where every page shows the refusal card.
    for (const [name, expected] of fixtureMountExpectations(journeyMount)) {
      results.push([`${name}, with the fixture mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
      results.push([`${name}, with no mount`, await run(apiOutput, null, { servedByApi, keyboard, step: expected.step, state: "refusal", refusalCode: "no_corpus_mounted" }, browser, liveRoot)]);
    }
    // The 404 page and a failure's retry (R60).
    for (const [name, expected, mounted] of failurePageExpectations()) {
      results.push([name, await run(apiOutput, mounted ? mount : null, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
    }
    // A search kept in the page's address, opened as a shared link (R109; the plan's item 3.5).
    for (const [name, expected] of addressExpectations()) {
      results.push([`${name}, with the fixture mount`, await run(apiOutput, mount, { servedByApi, keyboard, ...expected }, browser, liveRoot)]);
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
  const apiResults = realMount || licenceBlocked || twoState || europeAnnex || longAct ? [] : await apiJourneyRuns(apiOutput, mount);
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
