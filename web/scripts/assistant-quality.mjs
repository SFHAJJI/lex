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
 * The runs, oldest first: the date of the run, the release measured (its version's date part and its serving commit's
 * first eight characters), the cases asked and answered with the expected verdict, the quotations and those verified, the
 * questions asked twice with the same verdict, the answer time at the median and the 95th percentile, and what the run found.
 */
export const QUALITY_RUNS = Object.freeze([
  Object.freeze({
    date: '2026-11-11', release: '2026-11-11', commit: 'd8d38105', cases: 15, right: 9, quotes: 4, verified: 4,
    sameVerdict: 15, p50: 1.92, p95: 2.28, finding: 'eu',
  }),
  Object.freeze({
    date: '2026-11-12', release: '2026-11-12', commit: '8a5b5be5', cases: 15, right: 14, quotes: 12, verified: 12,
    sameVerdict: 15, p50: 2.08, p95: 2.48, finding: 'dora',
  }),
  Object.freeze({
    date: '2026-11-13', release: '2026-11-13', commit: 'c0191d7a', cases: 16, right: 14, quotes: 12, verified: 12,
    sameVerdict: 16, p50: 2.0, p95: 2.98, finding: 'insurance',
  }),
]);

/**
 * The latest run's results per question type, in the page's order: each type's key (its words are the page's), the cases
 * and those right.
 */
export const QUALITY_TYPES = Object.freeze([
  Object.freeze({ key: 'luArticle', cases: 4, right: 3 }),
  Object.freeze({ key: 'euArticle', cases: 5, right: 4 }),
  Object.freeze({ key: 'privacy', cases: 1, right: 1 }),
  Object.freeze({ key: 'clarify', cases: 2, right: 2 }),
  Object.freeze({ key: 'notHeld', cases: 1, right: 1 }),
  Object.freeze({ key: 'refuseAdvice', cases: 2, right: 2 }),
  Object.freeze({ key: 'language', cases: 1, right: 1 }),
]);

/**
 * The runs on the expected-article set (lex-eval 0.2, the owner's order of 2026-11-13, section D), oldest first: the 56
 * development questions, each with the act and article an answer must quote, and the 15 held-out questions, fixed before any
 * run, never tuned against and scored apart. Each run counts its results by kind (`QUALITY_OUTCOMES`), and states the release
 * measured, the answer times and the cost per question, which is an estimate (the prompt's and the plan's sizes at the
 * deployment's stated price per million tokens: the site reports no token count).
 */
export const QUALITY_OUTCOMES = Object.freeze(['rightArticle', 'wrongArticle', 'notFound', 'wronglyRefused', 'rightBehaviour', 'wrongBehaviour']);

export const QUALITY_SET_RUNS = Object.freeze([
  Object.freeze({
    date: '2026-11-14', release: '2026-11-13', commit: '17e2ab50', p50: 1.51, p95: 2.01, costEur: 0.0011,
    dev: Object.freeze({ cases: 56, rightArticle: 13, wrongArticle: 3, notFound: 23, wronglyRefused: 8, rightBehaviour: 9, wrongBehaviour: 0 }),
    heldout: Object.freeze({ cases: 15, rightArticle: 0, wrongArticle: 0, notFound: 9, wronglyRefused: 6, rightBehaviour: 0, wrongBehaviour: 0 }),
  }),
]);

/** The failures the latest run still shows, each a key whose words are the page's, in the page's order. */
export const QUALITY_KNOWN = Object.freeze(['adviceCues', 'englishOnFrench', 'euConcepts']);

/** A share as a whole percentage, rounded half up: 14 of 16 is 88. */
export const percent = (part, whole) => Math.floor((100 * part) / whole + 0.5);

/** The latest run. */
export const latestRun = () => QUALITY_RUNS.at(-1);
