// The assistant's measured results, release by release, as the Quality page shows them (2026-11-13, the owner's choice of
// a page of measured results for the Foyer interview). Each run is a lex-eval run against the live site
// (https://github.com/SFHAJJI/lex-eval, MLflow tracking and registry), copied here from its recorded metrics: the cases asked,
// the verdicts right, the quotations verified through the site's own `verify` operation, the questions whose second asking
// gave the same verdict, and the answer times. Nothing here is computed from another figure or estimated: a figure no run
// recorded (the cost of an answer) is not shown.
//
// This module is data and pure functions only.

/** The harness's public home, which the page names as the figures' source. */
export const QUALITY_SOURCE = 'https://github.com/SFHAJJI/lex-eval';

/**
 * The runs, oldest first: the date and time (UTC) of the run, the release measured (its version's date part and its serving commit's
 * first eight characters), the cases asked and answered with the expected verdict, the quotations and those verified, the
 * questions asked twice with the same verdict, the answer time at the median and the 95th percentile, and what the run found.
 */
export const QUALITY_RUNS = Object.freeze([
  Object.freeze({
    date: '2026-11-11', time: '22:48', release: '2026-11-11', commit: 'd8d38105', cases: 15, right: 9, quotes: 4, verified: 4,
    sameVerdict: 15, p50: 1.92, p95: 2.28, finding: 'eu',
  }),
  Object.freeze({
    date: '2026-11-12', time: '22:09', release: '2026-11-12', commit: '8a5b5be5', cases: 15, right: 14, quotes: 12, verified: 12,
    sameVerdict: 15, p50: 2.08, p95: 2.48, finding: 'dora',
  }),
  Object.freeze({
    date: '2026-11-13', time: '15:24', release: '2026-11-13', commit: 'c0191d7a', cases: 16, right: 14, quotes: 12, verified: 12,
    sameVerdict: 16, p50: 2.0, p95: 2.98, finding: 'insurance',
  }),
  Object.freeze({
    date: '2026-11-13', time: '21:14', release: '2026-11-13', commit: '17e2ab50', cases: 16, right: 15, quotes: 13, verified: 13,
    sameVerdict: 16, p50: 1.88, p95: 2.3, finding: 'insuranceFixed',
  }),
]);

/**
 * The latest run's results per question type, in the page's order: each type's key (its words are the page's), the cases
 * and those right.
 */
export const QUALITY_TYPES = Object.freeze([
  Object.freeze({ key: 'luArticle', cases: 4, right: 4 }),
  Object.freeze({ key: 'euArticle', cases: 5, right: 4 }),
  Object.freeze({ key: 'privacy', cases: 1, right: 1 }),
  Object.freeze({ key: 'clarify', cases: 2, right: 2 }),
  Object.freeze({ key: 'notHeld', cases: 1, right: 1 }),
  Object.freeze({ key: 'refuseAdvice', cases: 2, right: 2 }),
  Object.freeze({ key: 'language', cases: 1, right: 1 }),
]);

/** A share as a whole percentage, rounded half up: 14 of 16 is 88. */
export const percent = (part, whole) => Math.floor((100 * part) / whole + 0.5);

/** The latest run. */
export const latestRun = () => QUALITY_RUNS.at(-1);
