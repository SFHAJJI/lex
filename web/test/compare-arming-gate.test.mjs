// The compare-arming browser gate, held without a browser.
//
// `compareFailures` turns what the driven probe measured into failure sentences. The browser run
// proves the measurement; these tests prove the verdict: that each way arming can be unreachable,
// wrongly armed, silent about why, or invisible in the list produces a failure that names it, and
// that the sequence the preview now serves produces none.
//
// The second half binds the probe to what it drives. Its sentences are the component's own, read
// from the component rather than retyped, and its declared rows are rows the preview really
// carries, with the lex_ids that make arming reachable. A probe whose expectations drifted from
// the page would fail every browser combination for a reason no reader of the log could act on.

import assert from "node:assert/strict";
import test from "node:test";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

const WHERE = "search-react.html @390/light";

async function gate() {
  return import("../scripts/browser-evidence.mjs");
}

async function app() {
  return import("../.react-build/app.mjs");
}

const ROWS = Object.freeze({
  first: "Acte synthetique de demonstration, article 1",
  sameWork: "Acte synthetique de demonstration, article 1, etat anterieur",
  otherWork: "Annexe synthetique de demonstration",
});

/** What a working page shows at every step: the sentence expected, armed only for the pair. */
async function cleanSteps() {
  const { COMPARE_STEPS, COMPARE_SENTENCES } = await gate();
  return COMPARE_STEPS.map((step) => ({
    sentence: COMPARE_SENTENCES[step.expect],
    ariaDisabled: step.expect === "armed" ? "false" : "true",
    disabledAttr: false,
    selected: Object.fromEntries(
      Object.keys(ROWS).map((key) => [key, step.selected.includes(key) ? "true" : "false"]),
    ),
    matches: { first: 1, sameWork: 1, otherWork: 1 },
  }));
}

/** The clean sequence with one step's reading changed. */
async function withStep(index, change) {
  const steps = await cleanSteps();
  steps[index] = { ...steps[index], ...change(steps[index]) };
  return { rows: ROWS, steps };
}

test("the full drive of a page where arming works passes", async () => {
  const { compareFailures } = await gate();
  assert.deepEqual(compareFailures(WHERE, { rows: ROWS, steps: await cleanSteps() }), []);
});

test("a page with no compare control passes, because there is nothing to drive", async () => {
  const { compareFailures } = await gate();
  assert.deepEqual(compareFailures(WHERE, null), []);
});

test("a compare control with no declared rows fails as never driven", async () => {
  const { compareFailures } = await gate();
  assert.deepEqual(compareFailures(WHERE, { undeclared: true }), [
    `${WHERE}: a compare control is on this page and the compare probe declares no rows for it, ` +
      "so arming was never driven",
  ]);
});

test("a declared row the page does not carry exactly once fails as drift, and no step is judged", async () => {
  const { compareFailures } = await gate();
  for (const count of [0, 2]) {
    const failures = compareFailures(WHERE, { rows: ROWS, drift: { sameWork: count } });
    assert.deepEqual(failures, [
      `${WHERE}: the compare probe drives the row "${ROWS.sameWork}" and the page shows ${count} ` +
        "row(s) with that title; the fixture it was written for has changed, so arming was not driven",
    ]);
  }
});

test("two states that never arm fail as the armed state being unreachable", async () => {
  // The collapse: the second state shares the first's lex_id, so Space on it deselects the first.
  const { compareFailures, COMPARE_SENTENCES } = await gate();
  const failures = compareFailures(
    WHERE,
    await withStep(2, () => ({
      ariaDisabled: "true",
      sentence: COMPARE_SENTENCES.none,
      selected: { first: "false", sameWork: "false", otherWork: "false" },
    })),
  );
  assert.ok(
    failures.some((line) =>
      /with two states of one work selected \("[^"]+", "[^"]+"\), Compare stayed aria-disabled="true" and said "Select two states to compare them\."; the armed state is unreachable/.test(line)),
    JSON.stringify(failures),
  );
});

test("two different works that arm fail, naming why they must not", async () => {
  const { compareFailures, COMPARE_SENTENCES } = await gate();
  const failures = compareFailures(
    WHERE,
    await withStep(4, () => ({ ariaDisabled: "false", sentence: COMPARE_SENTENCES.armed })),
  );
  assert.deepEqual(failures, [
    `${WHERE}: with rows of two different works selected, Compare was armed (aria-disabled="false") ` +
      `and said "${COMPARE_SENTENCES.armed}"; two unrelated instruments are not states of each other`,
  ]);
});

test("three rows that say a pair is selected fail on the sentence", async () => {
  const { compareFailures, COMPARE_SENTENCES } = await gate();
  const failures = compareFailures(WHERE, await withStep(3, () => ({ sentence: COMPARE_SENTENCES.armed })));
  assert.deepEqual(failures, [
    `${WHERE}: with three rows selected, the compare control said "${COMPARE_SENTENCES.armed}", ` +
      `not "${COMPARE_SENTENCES.three}"`,
  ]);
});

test("a Space that selects nothing fails on the first press", async () => {
  const { compareFailures, COMPARE_SENTENCES } = await gate();
  const failures = compareFailures(
    WHERE,
    await withStep(1, () => ({
      sentence: COMPARE_SENTENCES.none,
      selected: { first: "false", sameWork: "false", otherWork: "false" },
    })),
  );
  assert.ok(
    failures.includes(
      `${WHERE}: after Space on one state, the compare control said "${COMPARE_SENTENCES.none}", ` +
        `not "${COMPARE_SENTENCES.one}"`,
    ),
    JSON.stringify(failures),
  );
});

test("rows that never say they are selected fail on the row", async () => {
  const { compareFailures } = await gate();
  const failures = compareFailures(
    WHERE,
    await withStep(1, () => ({ selected: { first: "false", sameWork: "false", otherWork: "false" } })),
  );
  assert.deepEqual(failures, [
    `${WHERE}: after Space on one state, row "${ROWS.first}" is aria-selected="false", not "true"; ` +
      "the list does not say which rows are armed",
  ]);
});

test("a Compare button carrying the disabled attribute fails, even when everything else is right", async () => {
  const { compareFailures } = await gate();
  const failures = compareFailures(WHERE, await withStep(0, () => ({ disabledAttr: true })));
  assert.deepEqual(failures, [
    `${WHERE}: at load, the Compare button carries the disabled attribute, so it leaves the Tab ` +
      "order and the reason it cannot be pressed is out of reach",
  ]);
});

test("an armed control is one defect, not two: the sentence it says is not judged separately", async () => {
  const { compareFailures, COMPARE_SENTENCES } = await gate();
  const failures = compareFailures(
    WHERE,
    await withStep(1, () => ({ ariaDisabled: "false", sentence: COMPARE_SENTENCES.armed })),
  );
  assert.equal(failures.length, 1, JSON.stringify(failures));
  assert.match(failures[0], /after Space on one state, Compare was armed .*one state is not a comparison$/);
});

// ---------------------------------------------------------------------------------------------
// The probe, bound to the component and to the preview it drives.
// ---------------------------------------------------------------------------------------------

const A = { lex_id: "lu-legilux:code-travail:2021-01-26" };
const B = { lex_id: "lu-legilux:code-travail:2021-04-23" };
const OTHER = { lex_id: "lu-legilux:code-civil:2021-01-26" };

test("the probe's sentences are the component's own", async () => {
  const { COMPARE_SENTENCES } = await gate();
  const { CompareArming, armingRefusal } = await app();
  const said = (selected) => {
    const html = renderToStaticMarkup(h(CompareArming, { selected, onCompare: () => {} }));
    const match = html.match(/<p class="compare-arming-state"[^>]*>([^<]*)<\/p>/);
    assert.ok(match, `no compare sentence rendered for ${selected.length} row(s)`);
    return match[1];
  };
  assert.equal(said([]), COMPARE_SENTENCES.none);
  assert.equal(said([A]), COMPARE_SENTENCES.one);
  assert.equal(said([A, B]), COMPARE_SENTENCES.armed);
  assert.equal(armingRefusal([A, B, OTHER]), COMPARE_SENTENCES.three);
  assert.equal(armingRefusal([A, OTHER]), COMPARE_SENTENCES.works);
});

test("the declared rows are the ones the probe expects", async () => {
  const { COMPARE_ROWS } = await gate();
  assert.deepEqual({ ...COMPARE_ROWS["search-react.html"] }, ROWS);
});

test("each declared row names exactly one row of the preview, and the three are three states", async () => {
  const { COMPARE_ROWS } = await gate();
  const { SEARCH_PREVIEW_HITS } = await app();
  assert.ok(Array.isArray(SEARCH_PREVIEW_HITS), "the preview's rows are not exported, so nothing binds the probe to them");
  const rows = COMPARE_ROWS["search-react.html"];
  const hit = {};
  for (const [key, title] of Object.entries(rows)) {
    const found = SEARCH_PREVIEW_HITS.filter((one) => one.title === title);
    assert.equal(found.length, 1, `"${title}" names ${found.length} row(s) of the preview`);
    hit[key] = found[0];
  }
  // Selection is keyed by lex_id, so two declared rows sharing one would toggle each other and the
  // drive would measure the collision rather than the rule.
  const ids = Object.values(hit).map((one) => one.lex_id);
  assert.equal(new Set(ids).size, ids.length, `the declared rows share a lex_id: ${JSON.stringify(ids)}`);
});

test("the declared rows arm and refuse the way the probe expects", async () => {
  const { COMPARE_ROWS, COMPARE_SENTENCES } = await gate();
  const { SEARCH_PREVIEW_HITS, armedBy, armingRefusal } = await app();
  assert.ok(Array.isArray(SEARCH_PREVIEW_HITS), "the preview's rows are not exported, so nothing binds the probe to them");
  const rows = COMPARE_ROWS["search-react.html"];
  const [first, sameWork, otherWork] = ["first", "sameWork", "otherWork"].map((key) =>
    SEARCH_PREVIEW_HITS.find((one) => one.title === rows[key]),
  );
  assert.equal(armedBy([first, sameWork]), true, "two states of one work in the preview do not arm");
  assert.equal(armingRefusal([first, otherWork]), COMPARE_SENTENCES.works);
  assert.equal(armingRefusal([first, sameWork, otherWork]), COMPARE_SENTENCES.three);
});
