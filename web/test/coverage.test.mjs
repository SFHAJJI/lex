// The coverage page, against the answer the platform actually sends.
//
// The page this replaced was written from a live V2 payload. It required `envelope.freshness
// .built_at` and stamped `Counts as of index build <instant>.` into its body and both its table
// captions; it printed `Observation history begins August 2026; replay depth grows from here.`; it
// had a whole section built on `document_types`; and it required `versions_with_text_served +
// versions_without_text === versions`. The V3 answer holds none of those members, records that no
// build time and no observation time are held, and states in its own `counts_note` that the two
// article columns "are not addends". So the page had to be rebuilt, and the question was what to
// build it against.
//
// A fixture written by hand is how the refusal catalogue came to teach a URL grammar no producer
// emits. So the answer comes from `schemas/v3-platform/answer-samples.json`, captured by driving
// the real handler. Two things about that file shape this test:
//
//   * it NORMALISES the values the fixture re-mints per run -- here the two mount digests -- to a
//     placeholder, so it is a reference for the SHAPE and the stable values rather than a
//     renderable answer. `withDigests` fills exactly those and nothing else.
//   * it is captured with `lu-legilux` coordinates, and the preview site promises every value on
//     it is synthetic, so the preview keeps synthetic values and is held to the captured SHAPE
//     instead.
//
// TWO RENDERERS, ONE VALIDATOR, AND WHY THIS FILE ABSORBED THE PARITY TEST. There used to be a
// `coverage-react.test.mjs` whose job was to feed every guard to both renderers and assert they
// refused the same inputs. It existed because each renderer carried its own copy of the rules, and
// it could only ever compare the rules that were in both places -- a rule added to neither was
// invisible to it. Both renderers now call `readCoverage`, so what is worth testing is no longer
// that the copies agree but that there is one copy: the refusal tests below assert both renderers
// throw the SAME MESSAGE, which a second implementation could not keep up with for long.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  HELD,
  NOT_STATED,
  narrowedNote,
  readCoverage,
  renderCoverage,
  unservedCapabilities,
  unservedCapabilityNote,
} from "../scripts/coverage.mjs";
import { PREVIEW_ANSWERS } from "../scripts/coverage-preview.mjs";
import { LIVE_EU_COVERAGE, LIVE_LU_COVERAGE } from "../scripts/live-coverage-record.mjs";
import { Coverage } from "../.react-build/app.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

/**
 * The paths a walk must have REACHED, not a count it must have exceeded.
 *
 * A floor is not a reach: the captured answer yields enough paths that a walk stopping after the
 * top level would clear any number worth writing down, and the part that matters is the part
 * furthest in. One shallow and two as deep as this answer goes, each in a different subtree, so a
 * walk that skipped any one of the three tables fails rather than passes smaller.
 */
const MUST_REACH = [
  "mounted.registry_sha256",
  "languages[].articles_without_publisher_date",
  "capability_cells[].population",
  "members.by_outcome[].outcome",
  "operations.not_served_operations[]",
  "not_held[].reason",
];

/** The only path on the captured answer the platform may send null. Nothing else may be. */
const NULLABLE = new Set([
  "requested_language",
  "languages[].first_state_date",
  "languages[].last_state_date",
]);

async function capturedAnswer() {
  let parsed;
  try {
    parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  } catch (error) {
    // Absent, unreadable or not JSON: this test measures nothing, and says so rather than passing.
    assert.fail(
      `the answer samples could not be read (${error.code ?? error.name}): this test proves ` +
        "nothing without them, and a green run here would mean the page had been checked",
    );
  }
  const row = parsed.sampled?.find((sample) => sample.operation === "coverage");
  assert.ok(row, "the census holds no coverage answer; this test has nothing to hold the page to");
  assert.equal(row.object_type, "coverage_report");
  return row.answer;
}

/**
 * Fills the fields the census normalises, and only those.
 *
 * Each gets a distinct digest, so a page printing one field's value in another field's row would
 * be caught rather than looking right.
 */
function withDigests(node, counter = { n: 0 }) {
  if (Array.isArray(node)) return node.map((item) => withDigests(item, counter));
  if (node && typeof node === "object") {
    return Object.fromEntries(
      Object.entries(node).map(([key, value]) => [key, withDigests(value, counter)]),
    );
  }
  if (node === PLACEHOLDER) {
    counter.n += 1;
    return (String(counter.n) + "0123456789abcdef".repeat(4)).slice(0, 64);
  }
  return node;
}

/** A deep copy with one path changed, so a mutant never leaks into the next test. */
function mutate(answer, change) {
  const copy = structuredClone(answer);
  change(copy);
  return copy;
}

const string = (answer) => renderCoverage(answer);
const react = (answer) => renderToStaticMarkup(h(Coverage, { answer }));

/**
 * The rendered text, with the entities both renderers use decoded.
 *
 * The platform's sentences carry apostrophes, and the two renderers escape them differently --
 * `&#39;` from this surface's own escaper, `&#x27;` from React. Asserting on raw markup would
 * compare a sentence against a spelling of itself and would pass or fail for a reason that has
 * nothing to do with whether the page said the thing.
 */
function text(html) {
  return html
    .replaceAll("&#39;", "'")
    .replaceAll("&#x27;", "'")
    .replaceAll("&quot;", '"')
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&amp;", "&");
}

function strip(html) {
  return text(html.replace(/<[^>]*>/g, " ")).replace(/\s+/g, " ").trim();
}

/**
 * The rows of one named table, as arrays of cell text.
 *
 * Keyed by caption rather than by position, because a page that rendered its language rows into
 * the members table would put every right value in a wrong place and a page-wide substring check
 * would not notice. Both renderers write their own markup and both write a `<caption>`.
 */
function tableRows(html, caption) {
  const tables = [...html.matchAll(/<table\b[^>]*>(.*?)<\/table>/gs)].map(([, body]) => body);
  const found = tables.find((body) => strip((body.match(/<caption>(.*?)<\/caption>/s) ?? [])[1] ?? "") === caption);
  assert.ok(found !== undefined, `no table is captioned ${JSON.stringify(caption)}`);
  return [...(found.match(/<tbody>(.*?)<\/tbody>/s) ?? [])[1].matchAll(/<tr>(.*?)<\/tr>/gs)]
    .map(([, row]) => [...row.matchAll(/<td>(.*?)<\/td>/gs)].map(([, value]) => strip(value)));
}

/** The captions a page carries, so a table dropped altogether fails rather than passes quietly. */
function captions(html) {
  return [...html.matchAll(/<caption>(.*?)<\/caption>/gs)].map(([, value]) => strip(value));
}

/** The labelled rows a page shows, as {label: visible text}, from either renderer's markup. */
function rows(html) {
  const found = new Map();
  const pattern = /<(?:th scope="row"|dt)>(.*?)<\/(?:th|dt)>\s*<(?:td|dd)>(.*?)<\/(?:td|dd)>/gs;
  for (const [, label, value] of html.matchAll(pattern)) {
    found.set(strip(label), strip(value));
  }
  return found;
}

/** The headings a page carries, in order. */
function headings(html) {
  return [...html.matchAll(/<h2>(.*?)<\/h2>/gs)].map(([, value]) => strip(value));
}

/**
 * Every field path an answer carries, so two answers can be compared by shape and not by value.
 *
 * The three walks below each add the element path for an array of PRIMITIVES, and the first draft
 * of all three did not. An array branch that only recurses reaches nothing when the items are
 * strings, so `languages_held[]`, `operations.served_operations[]` and
 * `operations.not_served_operations[]` -- three of the answer's eleven top-level members -- were
 * walked over and never looked at. `MUST_REACH` is what caught it, which is the argument for naming
 * a path rather than counting one: a floor of any size was cleared by a walk that skipped all three.
 */
function paths(node, prefix = "", found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) {
      if (item !== null && typeof item === "object") paths(item, `${prefix}[]`, found);
      else found.add(`${prefix}[]`);
    }
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const path = prefix.length === 0 ? key : `${prefix}.${key}`;
      found.add(path);
      paths(value, path, found);
    }
  }
  return found;
}

/** The coarse FORM of a value, so a preview holds synthetic values and still owes a real grammar. */
function form(value) {
  if (value === null) return "null";
  if (typeof value === "number") return "number";
  if (typeof value === "boolean") return "boolean";
  if (typeof value !== "string") return typeof value;
  if (/^[0-9a-f]{64}$/.test(value)) return "digest";
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return "date";
  if (/^[a-z][a-z0-9_]*$/.test(value)) return "token";
  return "text";
}

/** Every leaf of an answer, as path -> form. */
function forms(node, prefix = "", found = new Map()) {
  if (Array.isArray(node)) {
    for (const item of node) {
      if (item !== null && typeof item === "object") forms(item, `${prefix}[]`, found);
      else found.set(`${prefix}[]`, form(item));
    }
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const path = prefix.length === 0 ? key : `${prefix}.${key}`;
      if (value !== null && typeof value === "object") forms(value, path, found);
      else found.set(path, form(value));
    }
  }
  return found;
}

/** Every leaf of an answer, as path -> value, so a walk can look for the value itself. */
function leaves(node, prefix = "", found = []) {
  if (Array.isArray(node)) {
    for (const item of node) {
      if (item !== null && typeof item === "object") leaves(item, `${prefix}[]`, found);
      else found.push([`${prefix}[]`, item]);
    }
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const path = prefix.length === 0 ? key : `${prefix}.${key}`;
      if (value !== null && typeof value === "object") leaves(value, path, found);
      else found.push([path, value]);
    }
  }
  return found;
}

test("the page renders the answer the platform really sends", async () => {
  const answer = withDigests(await capturedAnswer());
  const html = string(answer);

  assert.deepEqual(headings(html), [
    "What this page is about",
    "How these counts are counted",
    "What this mount holds",
    "What the corpus recorded for its members",
    "What can be asked of this mount",
    "What this mount measured it can answer",
    "What this mount does not hold",
  ]);

  const shown = rows(html);
  assert.equal(shown.get("publisher"), answer.mounted.publisher);
  assert.equal(shown.get("corpus"), answer.mounted.corpus_sha256);
  assert.equal(shown.get("index"), answer.mounted.index_sha256);
  assert.equal(shown.get("operation registry"), answer.mounted.registry_sha256);
  assert.equal(shown.get("works"), String(answer.totals.works));
  assert.equal(shown.get("states"), String(answer.totals.states));
  assert.equal(shown.get("articles"), String(answer.totals.articles));
  assert.equal(shown.get("members"), String(answer.totals.members));
  assert.equal(shown.get("answered"), answer.operations.served_operations.join(" "));
  assert.equal(
    shown.get("registered, with no route on this mount"),
    answer.operations.not_served_operations.join(" "),
  );

  // Cell by cell against the answer's own rows, in the table captioned for them. A value in the
  // right table and the wrong column is the failure a page-wide substring check cannot see.
  assert.deepEqual(
    tableRows(html, "Held works, states and articles by language"),
    answer.languages.map((row) => [
      row.language,
      String(row.works),
      String(row.states),
      String(row.articles),
      HELD[row.searchable_text_held],
      String(row.articles_with_searchable_text),
      String(row.articles_without_publisher_date),
      row.first_state_date ?? NOT_STATED,
      row.last_state_date ?? NOT_STATED,
    ]),
  );
  assert.deepEqual(
    tableRows(html, "Members by the outcome the corpus recorded"),
    answer.members.by_outcome.map((row) => [row.outcome, String(row.members)]),
  );
  assert.deepEqual(
    tableRows(html, "Gap tokens the corpus recorded, counted by member"),
    answer.members.gaps.map((row) => [row.gap, String(row.members)]),
  );
  assert.deepEqual(
    tableRows(html, "Measured capabilities, by operation, column, field, language and period"),
    answer.capability_cells.map((row) => [
      row.operation, row.column, row.field, row.language,
      row.period_from, row.period_to, String(row.population),
    ]),
  );

  // The two counts that appear only inside prose, pinned in the sentence that carries them. The
  // leaf walk below cannot hold these: both are small numbers, and asking whether "3" is anywhere
  // on the page is a question the page answers whatever it says.
  assert.ok(text(html).includes(
    `${answer.members.with_gaps} of ${answer.totals.members} members recorded a gap.`));
  assert.ok(text(html).includes(
    `${answer.operations.served_operations.length} of ${answer.operations.registered} registered `
      + "operations are answered here."));

  // The platform's four sentences, verbatim and unedited. They are this service's own account of
  // its limits, and a renderer that tidied one would be editing the disclosure rather than showing
  // it.
  for (const sentence of [
    answer.scope, answer.counts_note, answer.members.gaps_note, answer.operations.note,
  ]) {
    assert.ok(text(html).includes(sentence), "a platform sentence was not reproduced verbatim");
  }
  for (const held of answer.not_held) {
    assert.ok(text(html).includes(held.item), `${held.item} is missing from the page`);
    assert.ok(text(html).includes(held.reason), `${held.item} was listed with no reason`);
  }
});

/**
 * WHAT THIS WALK PROVES, AND WHAT IT ONLY LOOKS LIKE IT PROVES.
 *
 * It asks whether each leaf's value appears anywhere in the page, which is a strong question for a
 * digest or a sentence and a weak one for a small number: on the captured answer, `body.includes
 * ("1")` is true of almost any page. Measured rather than guessed -- 19 of the 96 leaves are
 * booleans or numbers of at most two digits, so a fifth of what this walk "proves" it proves
 * vacuously.
 *
 * Those 19 are not left to it. Seventeen are compared cell by cell or row by row in the first test
 * above: thirteen through `tableRows` against the language, outcome, gap and capability tables, and
 * the four `totals` through the `rows` map. The remaining two -- `members.with_gaps` and
 * `operations.registered` -- appear only inside prose, and are pinned there in the first test by
 * the sentence that carries them.
 *
 * So what this walk is for is the leaf nobody thought to check: it fails when the platform adds a
 * member and the page does not grow a place for it. That is worth having and it is not the same
 * claim as its name.
 */
test("every leaf the platform sends reaches the page, and the walk reaches the deep ones", async () => {
  const answer = withDigests(await capturedAnswer());
  const body = text(string(answer));
  const walked = new Set();

  for (const [path, value] of leaves(answer)) {
    walked.add(path);
    if (value === null) {
      assert.ok(NULLABLE.has(path), `${path} arrived null and the platform does not send null there`);
      continue;
    }
    const looked = typeof value === "boolean" ? HELD[value] : String(value);
    assert.ok(body.includes(looked), `${path} is on the answer and not on the page`);
  }

  // A named path, not a count. A floor is cleared by a walk that stopped early; these are not.
  for (const path of MUST_REACH) {
    assert.ok(walked.has(path), `the walk never reached ${path}, so it proved less than it claims`);
  }
  // The walk checked every one of them, rather than passing having checked none.
  assert.ok(walked.size > MUST_REACH.length, "the walk found fewer paths than it names");
});

test("the two claims the platform refuses to make are gone, and its reasons are on the page", async () => {
  const answer = withDigests(await capturedAnswer());
  for (const render of [string, react]) {
    const body = text(render(answer));

    // The retention sentence. The answer says no observation time is held; the V3 acquisition
    // contract "deliberately offers no way to express a retroactive observation, because the one
    // thing a period rule can be twisted into is a claim that we watched something before we did",
    // and August 2026 is a date this platform does not supply.
    assert.ok(!body.includes("Observation history begins"), "the retention sentence survived");
    assert.ok(!body.includes("replay depth"), "the retention sentence survived in part");

    // The build instant, in the body and in every caption.
    assert.ok(!body.includes("Counts as of index build"), "the build stamp survived");
    assert.ok(!/\d{4}-\d{2}-\d{2}T/.test(body), "an instant reached a page that holds none");
    for (const caption of captions(render(answer))) {
      assert.ok(!/\bbuild\b/i.test(caption), `a caption still dates itself: ${caption}`);
    }

    // AND THE CALENDAR DATES ARE STILL THERE, which is the other half of the claim and the half
    // easier to get wrong. What went is a date OF THE COUNTS; the publisher's own dates -- each
    // language's state range and each measured capability's period -- are facts about the law and
    // stay. A page that dropped them to satisfy the rule above would have obeyed the words and
    // lost the point.
    for (const row of answer.languages) {
      if (row.first_state_date !== null) assert.ok(body.includes(row.first_state_date));
      if (row.last_state_date !== null) assert.ok(body.includes(row.last_state_date));
    }
    for (const measured of answer.capability_cells) {
      assert.ok(body.includes(measured.period_from), "a measured period lost its start");
      assert.ok(body.includes(measured.period_to), "a measured period lost its end");
    }
    assert.ok(answer.capability_cells.length > 0, "the captured answer measures no period, so this checked none");
    assert.ok(/\d{4}-\d{2}-\d{2}/.test(body), "the page carries no calendar date at all");

    // And the platform's own two reasons, which are what stands in their place.
    assert.ok(body.includes("no build time of the corpus or index is held"));
    assert.ok(body.includes("no observation time or first-sighting event is held"));
    assert.ok(body.includes(answer.mounted.corpus_sha256));
    assert.ok(body.includes(answer.mounted.index_sha256));
  }
});

test("the payload this page was written against before V3 is an error, not a degraded page", () => {
  // The captured V2 bytes, from `scripts/live-coverage-record.mjs`. Feeding them here is the one
  // thing they can still prove about code: that the retired shape is refused by name rather than
  // rendering a page quietly missing its date.
  for (const [name, retired] of [["lu-legilux", LIVE_LU_COVERAGE], ["eu-eurlex", LIVE_EU_COVERAGE]]) {
    for (const render of [string, react]) {
      assert.throws(
        () => render(retired),
        /carries envelope, which belongs to the payload this page was written against before V3/,
        `${name} rendered rather than being refused`,
      );
    }
  }

  // Each of the ten members by name, one at a time, so the guard is proved member by member rather
  // than by one payload that happens to carry the first of them.
  const retired = [
    "envelope", "publisher_name", "works", "versions", "text", "valid_from_earliest",
    "valid_from_latest", "known_gaps", "document_types", "document_types_total",
  ];
  for (const member of retired) {
    assert.throws(
      () => readCoverage({ [member]: 1 }),
      new RegExp(`carries ${member}, which belongs to the payload`),
      `${member} was not refused by name`,
    );
  }

  // The eleventh member the old page required is `languages`, which V3 sends under the same name
  // with a different row shape, so it cannot be refused by name and is caught by the row reader.
  assert.throws(
    () => readCoverage(mutate(PREVIEW_ANSWERS[0].answer, (answer) => {
      answer.languages = [{ code: "fr", works: 1, versions: 1 }];
    })),
    /keyed by code rather than by language/,
  );
});

test("the language table reconciles per column, because it is a partition in two and an overlap in one", async () => {
  const answer = withDigests(await capturedAnswer());
  const whole = PREVIEW_ANSWERS[0].answer;

  // STATES PARTITION. Every state row carries exactly one language, so with nothing narrowed away
  // the rows account for every state exactly once.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].states -= 1; })),
    /accounts for 17 states against a total of 18/,
  );

  // AND THE PARTITION IS CONDITIONAL. The same shortfall on a narrowed answer is correct, because
  // narrowing drops rows and leaves the totals whole. A rule that fired here would refuse every
  // language-narrowed answer the platform sends.
  assert.equal(typeof string(PREVIEW_ANSWERS[1].answer), "string");
  assert.ok(
    PREVIEW_ANSWERS[1].answer.languages.reduce((sum, row) => sum + row.states, 0)
      < PREVIEW_ANSWERS[1].answer.totals.states,
    "the narrowed preview stopped falling short of the totals, so it no longer tests this",
  );

  // WORKS OVERLAP. A work published in two languages is one work in two rows, so the rows sum past
  // the total and that is the correct shape. Measured on the whole-mount preview and asserted,
  // because a rule invented for the partition would quietly delete this page.
  assert.ok(
    whole.languages.reduce((sum, row) => sum + row.works, 0) > whole.totals.works,
    "the whole-mount preview stopped summing past its own headline, so it no longer tests this",
  );
  assert.equal(typeof string(whole), "string");

  // The per-row bound holds on every column, narrowed or not.
  for (const column of ["works", "states", "articles"]) {
    assert.throws(
      () => readCoverage(mutate(whole, (a) => { a.languages[0][column] = a.totals[column] + 1; })),
      new RegExp(`languages\\[0\\].${column} is ${whole.totals[column] + 1} against ${whole.totals[column]}`),
      `a language row claiming more ${column} than the mount holds was rendered`,
    );
  }

  // ARTICLES ARE BOUNDED AND NOT IDENTIFIED. A language present in `articles` and absent from
  // `states` gets no row, so the rows may fall short; they may never exceed.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.totals.articles = 239; })),
    /the articles the language breakdown accounts for is 240 against 239/,
  );
  assert.equal(
    typeof string(mutate(whole, (a) => { a.totals.articles = 300; })),
    "string",
    "the rows falling short of the article total was refused, and it is allowed to",
  );

  // The captured answer's own single row is a partition of one, so it is the case this rule was
  // first measured on rather than a case it was written for.
  assert.equal(
    answer.languages.reduce((sum, row) => sum + row.states, 0), answer.totals.states);
});

test("the two article columns are not added, because the platform says they are not addends", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  const row = whole.languages[0];
  assert.notEqual(
    row.articles_with_searchable_text + row.articles_without_publisher_date,
    row.articles,
    "the preview's own row stopped being a case where the two columns do not add up",
  );
  // Rendered, not refused. The V2 page REQUIRED this sum of its own two text columns, so on that
  // rule this answer -- and every honest answer -- would not have a page at all.
  assert.equal(typeof string(whole), "string");
  assert.ok(text(string(whole)).includes("are not addends"));

  // Each column is still bounded by the articles of its own row.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].articles_without_publisher_date = 201; })),
    /articles_without_publisher_date is 201 against 200/,
  );
});

test("the searchable-text flag and the count beside it cannot disagree, in either direction", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  // A cell with no measured support is refused where cells are built, so the flag is true exactly
  // when the count is above zero. Both directions, because only one of them is the comfortable one.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[1].searchable_text_held = true; })),
    /says searchable text is held and counts 0 articles with searchable text/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].searchable_text_held = false; })),
    /says searchable text is not held and counts 180 articles with searchable text/,
  );
  // And the flag is shown, so a reader can check that equality rather than take this page's word.
  const shown = tableRows(string(whole), "Held works, states and articles by language");
  assert.equal(shown[0][4], HELD.true);
  assert.equal(shown[1][4], HELD.false);
});

test("a date range that runs backwards, or holds one end only, is a wrong range and not a small one", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].first_state_date = "2030-01-01"; })),
    /reports states running from 2030-01-01 to 2029-11-30, which ends before it begins/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].last_state_date = null; })),
    /holds one end of its date range and not the other/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[1].first_state_date = "1900-01-01"; })),
    /holds one end of its date range and not the other/,
  );
  // A language holding neither end prints the sentence in both cells rather than leaving them
  // blank, because a blank reads as a fact about the corpus.
  const shown = tableRows(string(whole), "Held works, states and articles by language");
  assert.equal(shown[1][7], NOT_STATED);
  assert.equal(shown[1][8], NOT_STATED);
});

test("the members breakdowns reconcile differently, and the SQL behind them says which is which", () => {
  const whole = PREVIEW_ANSWERS[0].answer;

  // Outcomes partition: one member, one outcome, grouped over the same table the total counts.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.members.by_outcome[0].members -= 1; })),
    /the outcome breakdown accounts for 14 members against a total of 15/,
  );

  // Gap tokens overlap: a member carrying two tokens is counted in two rows, so they are never
  // summed. Each is bounded by the members carrying any gap, which is tighter than the total.
  assert.equal(
    typeof string(mutate(whole, (a) => {
      a.members.gaps = [{ gap: "point", members: 4 }, { gap: "second_token", members: 3 }];
    })),
    "string",
    "two gap rows summing past the members with gaps was refused, and it is allowed to",
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.members.gaps[0].members = 5; })),
    /members\.gaps\[0\]\.members is 5 against 4/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.members.with_gaps = 16; })),
    /members\.with_gaps is 16 against 15/,
  );
  // A `with_gaps` of zero beside a gap row fails that same bound, in its own terms, so there is no
  // separate rule for it. The first draft had one, and it was unreachable: for any gap row counting
  // a member at all, the bound fires first.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.members.with_gaps = 0; })),
    /members\.gaps\[0\]\.members is 4 against 0/,
  );

  // Both breakdowns are `GROUP BY`s, so a group exists because a member is in it and neither can
  // honestly count nought. That case IS reachable, and is the one the unreachable rule was reaching
  // for.
  for (const [what, change] of [
    ["an outcome", (a) => { a.members.by_outcome[1].members = 0; a.members.by_outcome[0].members = 15; }],
    ["a gap token", (a) => { a.members.gaps[0].members = 0; }],
  ]) {
    assert.throws(
      () => readCoverage(mutate(whole, change)),
      /counts no members for .*a row accounting for nobody is a category nothing recorded/s,
      `${what} counted by nobody was rendered`,
    );
  }

  // One direction only, and the other is rendered rather than refused: a member whose gap list is
  // written `[ ]` is counted in `with_gaps` and yields no token.
  const noTokens = string(mutate(whole, (a) => { a.members.gaps = []; }));
  assert.ok(text(noTokens).includes("No gap token is counted here"));
});

test("the operations census adds up, and says so where it does not", () => {
  const whole = PREVIEW_ANSWERS[0].answer;

  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.operations.registered = 26; })),
    /11 served and 16 unrouted operations are listed against 26 registered/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.operations.served_operations.push("ask"); })),
    /ask is listed both as served and as having no route/,
  );
  // This answer exists, so the operation that produced it is one this mount answers.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => {
      a.operations.served_operations = a.operations.served_operations.filter((o) => o !== "coverage");
      a.operations.registered -= 1;
    })),
    /the served operations do not include coverage, and this is a coverage answer/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.operations.served_operations.push("coverage"); })),
    /the served operations lists "coverage" twice/,
  );
});

test("a measured capability for an operation this mount does not serve is said, never hidden", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  // The preview does not teach this shape -- a preview page carrying a contradiction would be
  // teaching one -- so it is constructed here, where a test is allowed to build what a page is not.
  const contradiction = mutate(whole, (a) => { a.capability_cells[0].operation = "ask"; });
  const view = readCoverage(contradiction);
  assert.deepEqual(unservedCapabilities(view), ["ask"]);
  for (const render of [string, react]) {
    assert.ok(text(render(contradiction)).includes(unservedCapabilityNote(["ask"])));
  }
  // And nothing is said when there is nothing to say.
  assert.deepEqual(unservedCapabilities(readCoverage(whole)), []);
  assert.ok(!text(string(whole)).includes("which this mount does not serve"));
});

test("a capability cell is a measured one: a real period, and support it actually measured", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.capability_cells[0].population = 0; })),
    /advertises a capability with a population of zero/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.capability_cells[0].period_from = "2030-01-01"; })),
    /covers 2030-01-01 to 1999-12-31, which ends before it begins/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => {
      a.capability_cells[1] = { ...a.capability_cells[0] };
    })),
    /the measured capabilities lists/,
  );
});

test("narrowing narrows the rows and not the totals, and the page says so where the totals are", () => {
  const narrowed = PREVIEW_ANSWERS[1].answer;
  const absent = PREVIEW_ANSWERS[2].answer;
  const whole = PREVIEW_ANSWERS[0].answer;

  for (const render of [string, react]) {
    const body = text(render(narrowed));
    assert.ok(body.includes(narrowedNote("fra")));
    // The note sits with the totals, which is the number it is about: everything before the
    // language table, and after the totals themselves.
    const totals = body.indexOf("members");
    assert.ok(body.indexOf(narrowedNote("fra")) > totals);
    // And the whole-mount answer says nothing, because there is nothing to say.
    assert.ok(!text(render(whole)).includes("was narrowed to"));
  }

  // The rows are that language's and no others.
  assert.throws(
    () => readCoverage(mutate(narrowed, (a) => { a.languages.push({ ...whole.languages[1] }); })),
    /"fra" was asked for and the breakdown counts "deu"/,
  );
  // A row outside the languages this mount records holding is a row from somewhere else.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[1].language = "ltz"; })),
    /counts "ltz", which is not among the languages this mount records holding/,
  );

  // Narrowed to a language the mount does not hold: sentences, not two empty tables. The page this
  // replaced refused an empty language list outright, so it could not express this case at all.
  for (const render of [string, react]) {
    const body = text(render(absent));
    assert.ok(body.includes("No language has a row here"));
    assert.ok(body.includes("No capability was measured"));
    // The languages held are still listed, because that list is not narrowed.
    for (const language of absent.languages_held) assert.ok(body.includes(language));
    assert.ok(body.includes(String(absent.totals.works)));
  }
  assert.deepEqual(captions(string(absent)), ["Members by the outcome the corpus recorded",
    "Gap tokens the corpus recorded, counted by member"]);

  // With nothing narrowed away, every language held has a row and a table that stops is refused.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages.pop(); })),
    /no language was asked for and 1 of the 2 languages this mount holds have a row/,
  );
});

test("an answer that holds everything, or says nothing about what it does not hold, is refused", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.not_held = []; })),
    /an empty list would read as an answer that holds everything/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.not_held[0].reason = "  "; })),
    /not_held\[0\].reason is not a value this page can print/,
  );
  // A member that is absent and a member that is empty are different facts.
  for (const member of ["scope", "counts_note", "requested_language", "languages_held", "mounted",
    "totals", "languages", "members", "operations", "capability_cells", "not_held"]) {
    assert.throws(
      () => readCoverage(mutate(whole, (a) => { delete a[member]; })),
      new RegExp(`does not carry ${member}`),
      `${member} was allowed to be absent`,
    );
  }
  // A mount cannot hold more works than the states they were counted from.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.totals.works = 19; })),
    /totals\.works is 19 against 18/,
  );
});

test("both renderers refuse the same inputs with the same words, because there is one validator", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  const mutants = [
    (a) => { a.languages[0].states -= 1; },
    (a) => { a.members.by_outcome[0].members -= 1; },
    (a) => { a.operations.registered = 26; },
    (a) => { a.capability_cells[0].population = 0; },
    (a) => { a.languages[1].searchable_text_held = true; },
    (a) => { a.totals.works = 19; },
    (a) => { a.not_held = []; },
    (a) => { delete a.counts_note; },
  ];
  for (const change of mutants) {
    const mutant = mutate(whole, change);
    let fromString;
    let fromReact;
    assert.throws(() => string(mutant), (error) => { fromString = error.message; return true; });
    assert.throws(() => react(mutant), (error) => { fromReact = error.message; return true; });
    assert.equal(fromReact, fromString, "the two renderers refused for different reasons");
  }
  assert.equal(mutants.length, 8);
});

test("the two renderers show the same rows, the same tables and the same sentences", async () => {
  for (const answer of [withDigests(await capturedAnswer()), ...PREVIEW_ANSWERS.map((p) => p.answer)]) {
    const fromString = string(answer);
    const fromReact = react(answer);
    assert.deepEqual([...rows(fromReact)], [...rows(fromString)]);
    assert.deepEqual(captions(fromReact), captions(fromString));
    assert.deepEqual(headings(fromReact), headings(fromString));
    for (const caption of captions(fromString)) {
      assert.deepEqual(tableRows(fromReact, caption), tableRows(fromString, caption));
    }
    assert.deepEqual(
      strip(fromReact).split(" ").filter((word) => word.length > 0),
      strip(fromString).split(" ").filter((word) => word.length > 0),
    );
  }
});

test("the preview holds synthetic values in the shape the platform really sends", async () => {
  // The FILLED captured answer, because the census normalises the two mount digests to a
  // placeholder whose form is text. Compared against the unfilled one, this test would require the
  // preview to hold text where the platform sends a digest -- which is the opposite of its job.
  const captured = withDigests(await capturedAnswer());
  const expected = paths(captured);
  const expectedForms = forms(captured);

  for (const preview of PREVIEW_ANSWERS) {
    const answer = preview.answer;
    // Every path the platform sends, and no path it does not. A preview that grew a member is
    // teaching a shape nothing emits; one that lost a member is previewing a page that cannot be
    // reached.
    const actual = paths(answer);
    const narrowedAway = answer.languages.length === 0
      ? [...expected].filter((path) => path.startsWith("languages[]") || path.startsWith("capability_cells[]"))
      : [];
    assert.deepEqual(
      [...expected].filter((path) => !narrowedAway.includes(path)).sort(),
      [...actual].sort(),
      `${preview.heading} does not carry the paths the platform sends`,
    );

    // And the same grammar at each of them, so a preview cannot hold a token where the platform
    // sends a sentence, or a truncated digest where it sends a whole one.
    for (const [path, shape] of forms(answer)) {
      if (NULLABLE.has(path) && shape === "null") continue;
      const expectedShape = expectedForms.get(path);
      if (expectedShape === undefined) continue;
      if (expectedShape === "null") continue;
      assert.equal(shape, expectedShape, `${preview.heading} sends ${shape} at ${path}`);
    }
  }
});

test("the preview's capability cells sum to the count the platform derives from them", () => {
  // The producer computes `articles_with_searchable_text` as the sum of the search/articles/
  // searchable_text cells for that language. A preview where the two disagreed would be teaching a
  // shape the handler cannot produce, which is the defect this bridge exists to prevent.
  for (const preview of PREVIEW_ANSWERS) {
    for (const row of preview.answer.languages) {
      const summed = preview.answer.capability_cells
        .filter((measured) => measured.operation === "search"
          && measured.column === "articles"
          && measured.field === "searchable_text"
          && measured.language === row.language)
        .reduce((sum, measured) => sum + measured.population, 0);
      assert.equal(
        summed, row.articles_with_searchable_text,
        `${preview.heading} disagrees with itself on ${row.language}`,
      );
    }
  }
});
