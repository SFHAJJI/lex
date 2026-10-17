// The home page's facts: today in Luxembourg and the coverage line (the specification of record's R13 and R11).
//
// The home page is rendered by the server from the API's coverage answer, as a law's page is from its own (R67): both
// servers ask `coverage` for `/fr/` and `/en/` (`sitePageFor`), render the page from that envelope (`ssr-entry.jsx`), and
// write into it the facts the page shows, which the browser hydrates the same tree from. The facts, not the envelope: a
// coverage answer counts every measured capability cell, which on a population runs to thousands of rows the home page
// never shows, so the page carries the few values it says, read from the answer by the coverage reader's own rules
// (`readCoverage`). A page served as built (no renderer, or a render that failed) asks `coverage` itself once loaded, and
// takes today from this browser's clock (`todayInLuxembourg`).
//
// - **Today** is the calendar date in Luxembourg when the server answered: the answer's `context.freshness.observed_at`,
//   read by the coded rule (`luxembourgDate`, plan 0.8), never the build's time.
// - **The coverage line** says only what the answer counts (R11: no count is a literal): the works, states and articles
//   of the mounted Luxembourg index (`totals`), "as of the index built on" the date of the build the mounted log records
//   as mounted (`history.snapshots_with_text`, retained as `mounted`), that build time's date in Luxembourg, as today is.
//   A mount whose log records no build says the counts with no date and says why; with no corpus mounted, the line is the
//   coverage page's own sentence for that refusal; an answer that cannot be read shows no line.
//
// Data and pure functions only: the page, its client entry and the server renderer share it.

import { LIVE_COVERAGE_REFUSAL_SENTENCES, coverageOutcome, startLiveCoverage } from "./live-coverage.mjs";
import { readCoverage } from "./coverage.mjs";
import { answerHandedWith } from "./v3-client.mjs";
import { isCalendarDate, luxembourgDate } from "./temporal.mjs";

/** The id of the data block that carries the facts the home page was rendered from. */
export const HOME_FACTS_ID = "lex-home-facts";

/** The coverage line's states: counted, no corpus mounted, or nothing to say (an answer that could not be read). */
export const COVERAGE_LINE_STATES = Object.freeze(["counted", "unmounted", "unavailable"]);

/** The coverage refusal sentence the line says with no corpus mounted, as the coverage page says it. */
export const UNMOUNTED_SENTENCE = LIVE_COVERAGE_REFUSAL_SENTENCES.no_corpus_mounted;

function dateInLuxembourg(instant) {
  try {
    return luxembourgDate(instant);
  } catch {
    return null;
  }
}

/** The coverage line, from a coverage view state (`coverageOutcome`). */
export function coverageLineOf(outcome) {
  if (outcome?.state === "success") {
    const view = readCoverage(outcome.answer);
    const mounted = view.history?.logRecordsBuilds === true
      ? view.history.snapshotsWithText.find((snapshot) => snapshot.retainedAs.includes("mounted"))
      : undefined;
    return Object.freeze({
      state: "counted",
      works: view.totals.works,
      states: view.totals.states,
      articles: view.totals.articles,
      builtOn: mounted === undefined ? null : dateInLuxembourg(mounted.builtAt),
    });
  }
  if (outcome?.state === "refusal" && outcome.code === "no_corpus_mounted") return Object.freeze({ state: "unmounted" });
  return Object.freeze({ state: "unavailable" });
}

/**
 * The home page's facts from the coverage envelope the server answered (`contract`, the census's): `{ today, coverage }`,
 * `today` the calendar date in Luxembourg when it answered, or null when the envelope names no instant the rule reads.
 */
export function homeFacts(envelope, contract) {
  const outcome = coverageOutcome(answerHandedWith("coverage", envelope, { contract, objectType: "coverage_report" }));
  const observedAt = envelope?.context?.freshness?.observed_at;
  return Object.freeze({
    today: typeof observedAt === "string" ? dateInLuxembourg(observedAt) : null,
    coverage: coverageLineOf(outcome),
  });
}

const isCount = (value) => Number.isInteger(value) && value >= 0;

/**
 * Facts read back from a page's block, held to their shape, or null when they are not the facts a home page is rendered
 * from (the page then hydrates as the page as built, and the browser reports the hydration that changed it).
 */
export function readHomeFactsValue(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return null;
  const { today, coverage } = value;
  if (!(today === null || isCalendarDate(today))) return null;
  if (coverage === null || typeof coverage !== "object" || !COVERAGE_LINE_STATES.includes(coverage.state)) return null;
  if (coverage.state === "counted") {
    if (![coverage.works, coverage.states, coverage.articles].every(isCount)) return null;
    if (!(coverage.builtOn === null || isCalendarDate(coverage.builtOn))) return null;
    return Object.freeze({
      today,
      coverage: Object.freeze({ state: "counted", works: coverage.works, states: coverage.states, articles: coverage.articles, builtOn: coverage.builtOn }),
    });
  }
  return Object.freeze({ today, coverage: Object.freeze({ state: coverage.state }) });
}

/** The facts a home page carries, as its client entry reads them, or null when the page carries none. */
export function readHomeFacts(document) {
  const block = document.getElementById(HOME_FACTS_ID);
  if (block === null) return null;
  try {
    return readHomeFactsValue(JSON.parse(block.textContent ?? ""));
  } catch {
    return null;
  }
}

/**
 * What a home page served as built does once loaded: asks `coverage` once and hands the coverage line to `onLine` when it
 * settles; returns the cancel the page calls when it unmounts.
 */
export function startHomeCoverage({ contract, fetchImpl, onLine }) {
  return startLiveCoverage({ contract, fetchImpl, onOutcome: (outcome) => onLine(coverageLineOf(outcome)) });
}
