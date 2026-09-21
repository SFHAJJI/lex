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
  NO_ARTICLE_OUTCOMES,
  capabilityAbsence,
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
  "members.article_outcomes[].disposition",
  "operations.not_served_operations[]",
  "not_held[].reason",
];

/**
 * The ONLY path the platform may send null, and it was three.
 *
 * `languages[].first_state_date` and `languages[].last_state_date` were on this list because the
 * reader accepted null there and the preview taught it. `states.applicability_date` is `NOT NULL`
 * and a language row exists only because `states GROUP BY language` produced a group, so MIN and
 * MAX over it are values. A nullable list is a claim about the producer, and two thirds of this one
 * was wrong.
 */
const NULLABLE = new Set(["requested_language"]);

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
  // NOT "every honest answer", which is what this said and what the module header now denies: an
  // answer whose every dated article is searchable does add up and would have rendered. What the
  // V2 rule would have refused is any answer holding one dated article whose text is not
  // searchable.
  assert.equal(typeof string(whole), "string");
  assert.ok(text(string(whole)).includes("are not addends"));

  // BOTH columns are bounded by the articles of their own row, and this asserted one while the
  // comment beside the code claimed two. Not addends is not the same as not bounded.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].articles_without_publisher_date = 201; })),
    /articles_without_publisher_date is 201 against 200/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].articles_with_searchable_text = 5000; })),
    /articles_with_searchable_text is 5000 against 200/,
  );
  // And a language cannot hold more works than the states they were counted from. Asserted on the
  // SECOND row, because on the first the mount-level bound is tighter and fires first: 12 works
  // against 12 held leaves no value that reaches the per-row rule. A rule a test cannot reach on
  // the row it picked is a rule that test does not exercise.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[1].works = 5; })),
    /languages\[1\]\.works is 5 against 3; a language’s works are the distinct works of its states/,
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

test("a date range that runs backwards is a wrong range, and a null end is a shape nothing sends", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[0].first_state_date = "2030-01-01"; })),
    /reports states running from 2030-01-01 to 2029-11-30, which ends before it begins/,
  );

  // NOT NULLABLE, read off the DDL rather than assumed. `states.applicability_date` is `NOT NULL`
  // and a language row exists only because `states GROUP BY language` produced a group, so MIN and
  // MAX over it are values. The reader used to accept null here and the page printed "not stated by
  // the platform" for it, which is a cell for an answer no producer can send.
  for (const end of ["first_state_date", "last_state_date"]) {
    assert.throws(
      () => readCoverage(mutate(whole, (a) => { a.languages[0][end] = null; })),
      new RegExp(`${end} is not a calendar date: null`),
      `a null ${end} was accepted, and the index cannot produce one`,
    );
  }

  // A date that is well formed and does not exist, and a year that is not a date.
  for (const [value, what] of [["1999-02-31", "a day that does not exist"], ["1800", "a bare year"]]) {
    assert.throws(
      () => readCoverage(mutate(whole, (a) => { a.languages[0].first_state_date = value; })),
      /is not a calendar date/,
      `${what} was accepted as a state date`,
    );
  }

  // Both ends are rendered as themselves, in the cells the header names.
  const shown = tableRows(string(whole), "Held works, states and articles by language");
  assert.equal(shown[1][7], PREVIEW_ANSWERS[0].answer.languages[1].first_state_date);
  assert.equal(shown[1][8], PREVIEW_ANSWERS[0].answer.languages[1].last_state_date);
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

  // ALL THREE breakdowns are `GROUP BY`s, so a group exists because something is in it and none of
  // them can honestly count nought. That case IS reachable, and is the one the unreachable rule was
  // reaching for.
  //
  // One sentence for all three, and it was two: `article_outcomes` arrived with its own copy of the
  // rule and its own wording, already drifted by a word ("accounting for nobody" against
  // "accounting for none"). The counted noun differs and is a parameter now, so what this asserts
  // per row is the noun plus the one shared clause.
  for (const [what, counted, change] of [
    ["an outcome", "members",
      (a) => { a.members.by_outcome[1].members = 0; a.members.by_outcome[0].members = 15; }],
    ["a gap token", "members", (a) => { a.members.gaps[0].members = 0; }],
    ["a disposition", "outcomes", (a) => { a.members.article_outcomes[0].outcomes = 0; }],
  ]) {
    assert.throws(
      () => readCoverage(mutate(whole, change)),
      new RegExp(`counts no ${counted} for [^;]*; these rows are a grouping of the ${counted}, `
        + 'and a group exists because one of them is in it, so a row accounting for none is a '
        + 'category nothing recorded'),
      `${what} counted by nothing was rendered`,
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
  // Every emitted period is a single day: `MeasureCapabilities` groups by (language, wording date)
  // and passes that one date as both ends. The preview carried multi-year ranges until that was
  // read off the producer, so this pins the shape rather than describing it.
  for (const measured of whole.capability_cells) {
    assert.equal(measured.period_from, measured.period_to, "a cell spans more than the day it measured");
  }
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.capability_cells[0].population = 0; })),
    /advertises a capability with a population of zero/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.capability_cells[0].period_to = "1900-01-01"; })),
    /covers 1972-03-04 to 1900-01-01, which ends before it begins/,
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
  const unmeasured = PREVIEW_ANSWERS[2].answer;
  const whole = PREVIEW_ANSWERS[0].answer;

  for (const render of [string, react]) {
    const body = text(render(narrowed));
    assert.ok(body.includes(narrowedNote("fra")));
    // PLACEMENT, tested as placement. The note belongs with the totals it qualifies, which means
    // after them and BEFORE the table whose rows are the narrowed ones. An earlier version of this
    // test asserted only that it came after the first occurrence of "members", which moving it to
    // the very end would also satisfy.
    // All three offsets are measured in ONE string, because an index into the markup and an index
    // into the stripped text are not comparable and the first version of this compared them.
    const flat = strip(render(narrowed));
    const note = flat.indexOf(narrowedNote("fra"));
    const totals = flat.indexOf("What this mount holds");
    const tableStart = flat.indexOf("Held works, states and articles by language");
    assert.ok(note > 0 && totals > 0 && tableStart > 0, "one of the three landmarks is not on the page");
    assert.ok(note > totals, "the narrowed note is above the totals it qualifies");
    assert.ok(note < tableStart, "the narrowed note is below the table it should introduce");
    assert.ok(!text(render(whole)).includes("was narrowed to"));
  }

  // The rows are that language's and no others.
  assert.throws(
    () => readCoverage(mutate(narrowed, (a) => { a.languages.push({ ...whole.languages[1] }); })),
    /"fra" was asked for and 2 languages have a row/,
  );
  assert.throws(
    () => readCoverage(mutate(whole, (a) => { a.languages[1].language = "ltz"; })),
    /counts "ltz", which is not among the languages this mount records holding/,
  );

  // A LANGUAGE THE MOUNT DOES NOT HOLD IS NOT AN ANSWER AT ALL. The producer refuses that request
  // with `language_not_available` before any answer is built (`V3CorpusMount.cs:1618-1628`), so the
  // reader must not accept one. It did, and a preview and a test described the resulting page --
  // a shape nothing emits, taught by the file whose header promises not to teach one.
  assert.throws(
    () => readCoverage(mutate(whole, (a) => {
      a.requested_language = "ltz";
      a.languages = [];
      a.capability_cells = [];
    })),
    /narrowed to "ltz", which is not among the languages this mount holds/,
  );

  // NARROWED TO A LANGUAGE THAT MEASURES NOTHING, which is the case the page got wrong. The mount
  // measured four capabilities and every one is the other language's; narrowing dropped them. The
  // sentence must be about the language and must not say the mount measured nothing.
  assert.equal(unmeasured.capability_cells.length, 0);
  assert.ok(whole.capability_cells.length > 0, "the whole mount measures nothing, so this proves nothing");
  // The two sentences as LITERALS, not as calls to the function under test. Asking the page and
  // `capabilityAbsence` the same question compares a thing with itself: a mutant that made the
  // function always return the narrowed form moved both sides and lived through this test.
  const ABOUT_THE_MOUNT = "This mount measured no capability";
  const ABOUT_THE_LANGUAGE = "No capability is measured for deu";
  assert.ok(capabilityAbsence(null).startsWith(ABOUT_THE_MOUNT));
  assert.ok(capabilityAbsence("deu").startsWith(ABOUT_THE_LANGUAGE));

  for (const render of [string, react]) {
    const body = text(render(unmeasured));
    assert.ok(body.includes(ABOUT_THE_LANGUAGE), "the narrowed absence sentence is missing");
    assert.ok(
      !body.includes(ABOUT_THE_MOUNT),
      "the page told a reader the mount measured nothing, and it measured four",
    );
    // And the unnarrowed sentence is still used where it IS true, so this is not simply the one
    // sentence renamed.
    const empty = mutate(whole, (a) => {
      a.capability_cells = [];
      for (const row of a.languages) {
        row.searchable_text_held = false;
        row.articles_with_searchable_text = 0;
      }
    });
    const unnarrowed = text(render(empty));
    assert.ok(unnarrowed.includes(ABOUT_THE_MOUNT), "the unnarrowed absence sentence is missing");
    assert.ok(!unnarrowed.includes(ABOUT_THE_LANGUAGE));
  }

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
    // A list narrowed to nothing takes its element paths with it, and each list narrows on its own:
    // the whole mount empties neither, a language that measures nothing empties only the cells, and
    // the first version of this keyed both lists off `languages` being empty.
    const actual = paths(answer);
    const narrowedAway = [
      ...(answer.languages.length === 0 ? ["languages[]"] : []),
      ...(answer.capability_cells.length === 0 ? ["capability_cells[]"] : []),
    ];
    const dropped = (path) => narrowedAway.some((prefix) => path.startsWith(prefix));
    assert.deepEqual(
      [...expected].filter((path) => !dropped(path)).sort(),
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

// ---------------------------------------------------------------------------------------------
// The protections the rebuild dropped.
//
// The page this replaced had a hostile-string test, keyboard-region and header assertions, count
// and calendar validity and a duplicate-row test per table. None of them survived the rewrite, and
// the writer seat found it by running 32 mutants against the whole suite: 20 lived, 18 of them
// here. Not one was a bug at the time -- the code escapes, refuses and separates correctly -- and
// that is the point. Nothing would have noticed, on the page whose header says it exists to be
// checked against.
//
// They are written as walks rather than as lists, because a list is what let them die: each named
// the V2 members it covered, so the members went and the tests went with them. A walk over the
// captured answer covers whatever the platform sends, including what it has not sent yet.
// ---------------------------------------------------------------------------------------------

/** What one path holds, so a walk can find every other path holding the same value. */
function pathValue(answer, path) {
  return stringLeavesValues(answer).get(path);
}

/** Every string leaf as path -> value. */
function stringLeavesValues(node, path = "", found = new Map()) {
  if (Array.isArray(node)) {
    node.forEach((item, index) => {
      if (item !== null && typeof item === "object") stringLeavesValues(item, `${path}[${index}]`, found);
      else if (typeof item === "string") found.set(`${path}[${index}]`, item);
    });
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const here = path.length === 0 ? key : `${path}.${key}`;
      if (value !== null && typeof value === "object") stringLeavesValues(value, here, found);
      else if (typeof value === "string") found.set(here, value);
    }
  }
  return found;
}

/** Every string leaf of an answer, as [path, setter], so a walk can make each one hostile. */
function stringLeaves(node, path = "", found = []) {
  if (Array.isArray(node)) {
    node.forEach((item, index) => {
      if (item !== null && typeof item === "object") stringLeaves(item, `${path}[${index}]`, found);
      else if (typeof item === "string") {
        found.push([`${path}[${index}]`, (value) => { node[index] = value; }]);
      }
    });
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const here = path.length === 0 ? key : `${path}.${key}`;
      if (value !== null && typeof value === "object") stringLeaves(value, here, found);
      else if (typeof value === "string") {
        found.push([here, (next) => { node[key] = next; }]);
      }
    }
  }
  return found;
}

test("every string the platform sends is escaped, wherever the page puts it", async () => {
  const captured = withDigests(await capturedAnswer());
  // A value that is a tag, an attribute break and a handler at once, so a leaf reaching text, an
  // attribute or a `<code>` fails on whichever it reaches.
  const HOSTILE = '<img src=x onerror="alert(1)">" \'';
  let walked = 0;

  for (const [path] of stringLeaves(structuredClone(captured))) {
    // THE VALUE, EVERYWHERE IT APPEARS, not the leaf alone. A language code is the same string in
    // `languages_held` and in its row, and the reader refuses an answer where those disagree, so
    // poisoning one leaf made the answer invalid and the walk skipped it -- which left the language
    // cell as the one string on the page with no escaping test. The writer seat's E6 mutant
    // survived exactly there. Poisoning the value keeps the answer self-consistent and reaches it.
    const held = pathValue(captured, path);
    const poison = (answer, replacement) => {
      const copy = structuredClone(answer);
      for (const [candidate, set] of stringLeaves(copy)) {
        if (candidate === path || pathValue(captured, candidate) === held) set(replacement);
      }
      return copy;
    };

    let rendered;
    const mutant = poison(captured, HOSTILE);
    try {
      rendered = [string(mutant), react(mutant)];
    } catch {
      // A leaf the reader refuses with a hostile value is a leaf this page never prints.
      continue;
    }
    walked += 1;
    const benign = poison(captured, "harmless");
    const clean = [string(benign), react(benign)];
    for (const [index, html] of rendered.entries()) {
      // THE STRUCTURAL QUESTION, because the textual one gives a false positive: correctly escaped
      // output still contains the literal characters `onerror=`, harmlessly, as text, and a first
      // version of this test failed the page for printing them. What matters is whether the value
      // OPENED anything. A hostile leaf must leave the page with exactly the markup a harmless leaf
      // leaves, so an injected tag or a broken-out attribute is a difference in the count of `<` or
      // of `"`, and nothing that is merely text can be.
      const angles = (markup) => (markup.match(/</g) ?? []).length;
      const quotes = (markup) => (markup.match(/"/g) ?? []).length;
      assert.equal(angles(html), angles(clean[index]), `${path} opened a tag`);
      assert.equal(quotes(html), quotes(clean[index]), `${path} broke out of an attribute`);
      assert.ok(html.includes("&lt;img"), `${path} was dropped rather than escaped`);
    }
  }
  // A walk that refused everything would pass every assertion above having checked nothing.
  assert.ok(walked > 20, `only ${walked} string leaves reached the page; the walk proved little`);
});

test("the scroll regions and the column headers are what a keyboard and a reader need", async () => {
  const captured = withDigests(await capturedAnswer());
  for (const render of [string, react]) {
    const html = render(captured);
    const boxes = [...html.matchAll(/<div class(?:Name)?="coverage-scroll"[^>]*>/g)].map(([tag]) => tag);
    assert.equal(boxes.length, captions(html).length, "a table is not in a scroll box");
    for (const box of boxes) {
      // A scrollable region is a tab stop whether or not it asks to be, so it carries a role and a
      // name rather than announcing nothing.
      assert.match(box, /tabindex="0"/i, `a scroll box is not focusable: ${box}`);
      assert.match(box, /role="region"/, `a scroll box has no role: ${box}`);
      assert.match(box, /aria-label="[^"]+, scrollable"/, `a scroll box has no name: ${box}`);
    }
    // Every column header is a header with a scope, which is what makes a cell announce its column.
    const headers = [...html.matchAll(/<th\b[^>]*>/g)].map(([tag]) => tag);
    assert.ok(headers.length > 0);
    for (const header of headers) {
      assert.match(header, /scope="(col|row)"/, `a header declares no scope: ${header}`);
    }
  }

  // ONE MUTANT IS NOT KILLED HERE AND THIS SAYS WHY. Removing `escapeHtml` from the caption
  // survives the whole suite, because every caption is a literal written in this file: no answer
  // value reaches one, so there is nothing for the escaping to do and no input that tells the two
  // versions apart. Manufacturing a kill would mean asserting that a constant is a constant.
  //
  // What is worth guarding is the assumption rather than the mutant: if a caption ever grew an
  // answer value, the escaping would start mattering and nothing would say so. So two answers with
  // different contents must produce the SAME captions, and this fails the day one stops being a
  // literal.
  const first = withDigests(await capturedAnswer());
  const second = PREVIEW_ANSWERS[0].answer;
  assert.deepEqual(
    captions(string(second)).filter((caption) => captions(string(first)).includes(caption)).length,
    captions(string(first)).length,
    "a caption changed with the answer, so it carries a value and must be escaped",
  );
});

test("a count is a count and a date is a date, on every member that is one", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  const counts = [
    ["totals.articles", (a, v) => { a.totals.articles = v; }],
    ["languages[0].articles", (a, v) => { a.languages[0].articles = v; }],
    ["languages[0].works", (a, v) => { a.languages[0].works = v; }],
    ["members.with_gaps", (a, v) => { a.members.with_gaps = v; }],
    ["members.gaps[0].members", (a, v) => { a.members.gaps[0].members = v; }],
    ["members.by_outcome[0].members", (a, v) => { a.members.by_outcome[0].members = v; }],
    ["operations.registered", (a, v) => { a.operations.registered = v; }],
    ["capability_cells[0].population", (a, v) => { a.capability_cells[0].population = v; }],
  ];
  for (const [path, set] of counts) {
    for (const value of [-7, 1.5, "3", null]) {
      assert.throws(
        () => readCoverage(mutate(whole, (a) => set(a, value))),
        /rather than a count/,
        `${path} accepted ${JSON.stringify(value)}`,
      );
    }
  }
  // And every date, including one that is well formed and does not exist.
  const dates = [
    ["languages[0].first_state_date", (a, v) => { a.languages[0].first_state_date = v; }],
    ["capability_cells[0].period_from", (a, v) => { a.capability_cells[0].period_from = v; }],
    ["capability_cells[0].period_to", (a, v) => { a.capability_cells[0].period_to = v; }],
  ];
  for (const [path, set] of dates) {
    for (const value of ["1999-02-31", "1800", "2024-13-01", "not a date", 20240201]) {
      assert.throws(
        () => readCoverage(mutate(whole, (a) => set(a, value))),
        /is not a calendar date/,
        `${path} accepted ${JSON.stringify(value)}`,
      );
    }
  }
});

test("one key is one row, in every keyed list on the page", () => {
  const whole = PREVIEW_ANSWERS[0].answer;
  // A repeated key is two figures for one thing and a reader cannot tell which governs. Every keyed
  // list the answer carries, not the two that happened to have a test.
  const duplicated = [
    ["the language breakdown", (a) => { a.languages.push({ ...a.languages[0] }); }],
    ["the outcome breakdown", (a) => { a.members.by_outcome.push({ ...a.members.by_outcome[0] }); }],
    ["the gap breakdown", (a) => { a.members.gaps.push({ ...a.members.gaps[0] }); }],
    ["the served operations", (a) => { a.operations.served_operations.push("coverage"); }],
    ["the operations with no route", (a) => { a.operations.not_served_operations.push("ask"); }],
    ["the measured capabilities", (a) => { a.capability_cells.push({ ...a.capability_cells[0] }); }],
    ["the languages this mount holds", (a) => { a.languages_held.push("fra"); }],
    ["the list of what is not held", (a) => { a.not_held.push({ ...a.not_held[0] }); }],
  ];
  for (const [what, change] of duplicated) {
    assert.throws(
      () => readCoverage(mutate(whole, change)),
      new RegExp(`${what} lists`),
      `${what} accepted a repeated key`,
    );
  }
  assert.equal(duplicated.length, 8, "a keyed list was added and this walk was not extended");
});

test("a date on this page is a currency claim, and the guard is a reach rather than a list", async () => {
  // THE ABSENCE GUARD THE FIRST VERSION SHOULD HAVE BEEN. It looked for the exact old strings --
  // `Counts as of index build`, `Observation history begins`, a `T` instant -- so a new currency
  // claim in other words passed. The writer seat proved it: both renderers printed "Counts as of
  // 2026-09-01." and the whole suite stayed green.
  //
  // This asks the opposite question. Every calendar date the page prints must be one the ANSWER
  // carries, so a date from anywhere else fails without this test knowing what words carry it.
  const captured = withDigests(await capturedAnswer());
  for (const answer of [captured, ...PREVIEW_ANSWERS.map((preview) => preview.answer)]) {
    const held = new Set([
      ...answer.languages.flatMap((row) => [row.first_state_date, row.last_state_date]),
      ...answer.capability_cells.flatMap((cell) => [cell.period_from, cell.period_to]),
    ]);
    for (const render of [string, react]) {
      const body = text(render(answer));
      for (const [printed] of body.matchAll(/\d{4}-\d{2}-\d{2}/g)) {
        assert.ok(held.has(printed), `${printed} is on the page and not in the answer`);
      }
      assert.ok(!/\d{2}:\d{2}:\d{2}/.test(body), "a time of day reached a page that holds none");
      assert.ok(!/\d{4}-\d{2}-\d{2}T/.test(body), "an instant reached a page that holds none");
    }
  }
  // And the guard reaches something: the captured answer does print dates, so a page that printed
  // none would pass the loop above having checked nothing.
  assert.ok(/\d{4}-\d{2}-\d{2}/.test(text(string(captured))), "the page prints no date at all");
});

test("the two renderers separate adjacent evidence values, which stripping tags hides", async () => {
  const captured = withDigests(await capturedAnswer());
  for (const answer of [captured, ...PREVIEW_ANSWERS.map((preview) => preview.answer)]) {
    // Tags REMOVED, not replaced by a space. The parity test replaces them, so `fra deu` and
    // `fradeu` read alike to it and React gluing two identifiers together was invisible. The CSS
    // records this same defect shipping once: "Chrome rendered ... as one word".
    const glue = (html) => text(html.replace(/<[^>]*>/g, "")).replace(/[^\S\n]+/g, " ").trim();
    assert.equal(glue(react(answer)), glue(string(answer)), "the two renderers space values differently");
    // And the separation is really there, rather than both renderers having lost it together.
    if (answer.languages_held.length > 1) {
      const [first, second] = answer.languages_held;
      for (const render of [string, react]) {
        assert.ok(
          glue(render(answer)).includes(`${first} ${second}`),
          "two adjacent evidence values are printed as one word",
        );
      }
    }
  }
});

test("the preview reproduces the platform's sentences, to the character", async () => {
  // M5: the comment said "verbatim" and one apostrophe was curly where the platform's is straight.
  // Nothing compared them, so "verbatim" was a promise the file made about itself.
  const captured = await capturedAnswer();
  for (const preview of PREVIEW_ANSWERS) {
    const answer = preview.answer;
    assert.equal(answer.scope, captured.scope, `${preview.heading} paraphrases scope`);
    assert.equal(answer.counts_note, captured.counts_note, `${preview.heading} paraphrases counts_note`);
    assert.equal(answer.members.gaps_note, captured.members.gaps_note);
    assert.equal(
      answer.members.article_outcomes_note, captured.members.article_outcomes_note,
      `${preview.heading} paraphrases article_outcomes_note`);
    assert.equal(answer.operations.note, captured.operations.note);
    assert.deepEqual(answer.not_held, captured.not_held, `${preview.heading} rewrote a not_held row`);
    assert.deepEqual(answer.operations.served_operations, captured.operations.served_operations);
    assert.deepEqual(
      answer.operations.not_served_operations, captured.operations.not_served_operations);
  }
});

const OUTCOMES_CAPTION = "Legal-content outcomes the corpus recorded, by disposition";

/**
 * The disposition tokens the corpus declares, read from the enum that declares them and not copied
 * beside it, so the preview cannot teach a token the corpus has never emitted. The file holds four
 * enums and a token of another one is not a disposition of this one, so the block is cut at the
 * enum's own closing brace.
 */
async function corpusDispositionTokens() {
  const source = await readFile(
    new URL("../../src/Lex.V3.Ingest/LexCorpus6Builder.cs", import.meta.url), "utf8");
  const start = source.indexOf("enum LexCorpus6Stage3Disposition");
  assert.notEqual(start, -1, "the corpus declares no enum LexCorpus6Stage3Disposition");
  const open = source.indexOf("{", start);
  const close = source.indexOf(String.fromCharCode(10) + "}", open);
  assert.ok(close > open, "the disposition enum has no closing brace");
  const tokens = new Set(
    [...source.slice(open, close).matchAll(/JsonStringEnumMemberName\("([a-z0-9_]+)"\)/g)]
      .map(([, token]) => token),
  );
  assert.ok(tokens.has("akn_admitted"), "the disposition vocabulary did not parse");
  assert.equal(tokens.has("acquired"), false, "the disposition vocabulary is not the outcome one");
  return tokens;
}

test("the corpus's legal-content outcomes are printed token by token, in the order sent, in both renderers", async () => {
  const answer = withDigests(await capturedAnswer());
  // The committed real document: 54 top-level articles, 49 admitted and five the reviewed profile
  // could not represent. A page that printed only the held ones would hide the five.
  assert.deepEqual(
    answer.members.article_outcomes.map((row) => `${row.disposition}=${row.outcomes}`),
    ["akn_admitted=49", "akn_unsupported_content_shape=5"],
    "the captured answer no longer carries the outcomes this test is about",
  );
  // More rows than the capture has, and a token nobody has seen: printed as it came, in the order
  // the platform sent, and none of them glossed.
  const three = mutate(answer, (a) => {
    a.members.article_outcomes = [
      { disposition: "akn_admitted", outcomes: 3 },
      { disposition: "akn_marker_only_evidence", outcomes: 2 },
      { disposition: "akn_a_token_from_a_later_corpus", outcomes: 1 },
    ];
  });
  for (const [name, given] of [["the capture", answer], ["three rows", three]]) {
    for (const [renderer, html] of [["string", string(given)], ["react", react(given)]]) {
      assert.deepEqual(
        tableRows(html, OUTCOMES_CAPTION),
        given.members.article_outcomes.map((row) => [row.disposition, String(row.outcomes)]),
        `${renderer}: ${name}: the outcome rows are not each token's own, in order`,
      );
      assert.ok(
        text(html).includes(given.members.article_outcomes_note),
        `${renderer}: ${name}: the platform's note on the outcomes was not printed verbatim`,
      );
    }
  }
});

test("a malformed article-outcomes member is refused by the one reader, and nothing is printed", async () => {
  const answer = withDigests(await capturedAnswer());
  const cases = [
    ["absent", (a) => { delete a.members.article_outcomes; }, /members does not carry article_outcomes/],
    ["null", (a) => { a.members.article_outcomes = null; }, /is a list, even an empty one/],
    ["an object", (a) => { a.members.article_outcomes = { akn_admitted: 49 }; }, /is a list, even an empty one/],
    ["a row that is not an object", (a) => { a.members.article_outcomes = [49]; }, /disposition is not a value this page can print/],
    ["a row with no token", (a) => { a.members.article_outcomes = [{ outcomes: 1 }]; }, /disposition is not a value this page can print/],
    ["a row with an empty token", (a) => { a.members.article_outcomes = [{ disposition: " ", outcomes: 1 }]; }, /disposition is not a value this page can print/],
    ["a negative count", (a) => { a.members.article_outcomes[0].outcomes = -1; }, /rather than a count/],
    ["a fractional count", (a) => { a.members.article_outcomes[0].outcomes = 1.5; }, /rather than a count/],
    ["a count as text", (a) => { a.members.article_outcomes[0].outcomes = "49"; }, /rather than a count/],
    ["a row with no count", (a) => { delete a.members.article_outcomes[0].outcomes; }, /does not carry outcomes/],
    [
      "one token twice",
      (a) => { a.members.article_outcomes = [
        { disposition: "akn_admitted", outcomes: 1 }, { disposition: "akn_admitted", outcomes: 2 }]; },
      /the article outcome breakdown lists "akn_admitted" twice/,
    ],
    [
      "a token counting no outcome",
      (a) => { a.members.article_outcomes[1].outcomes = 0; },
      /counts no outcomes for "akn_unsupported_content_shape".*a row accounting for none is a category nothing recorded/s,
    ],
    ["no note", (a) => { delete a.members.article_outcomes_note; }, /members does not carry article_outcomes_note/],
    ["an empty note", (a) => { a.members.article_outcomes_note = " "; }, /article_outcomes_note is not a value this page can print/],
  ];
  for (const [name, change, pattern] of cases) {
    assert.throws(() => readCoverage(mutate(answer, change)), pattern, `the reader accepted ${name}`);
    assert.throws(() => string(mutate(answer, change)), pattern, `the string renderer accepted ${name}`);
    assert.throws(() => react(mutate(answer, change)), pattern, `the React port accepted ${name}`);
  }
});

test("a mount whose members recorded no legal-content outcome says so, and still prints the platform's note", async () => {
  const answer = mutate(withDigests(await capturedAnswer()), (a) => { a.members.article_outcomes = []; });
  for (const [renderer, html] of [["string", string(answer)], ["react", react(answer)]]) {
    assert.ok(text(html).includes(NO_ARTICLE_OUTCOMES), `${renderer} left the empty list unexplained`);
    assert.equal(
      captions(html).includes(OUTCOMES_CAPTION), false, `${renderer} printed a table with no rows`);
    assert.ok(text(html).includes(answer.members.article_outcomes_note), `${renderer} dropped the note`);
  }
});

test("the preview's outcome tokens are the corpus's and its held outcomes are its articles", async () => {
  const tokens = await corpusDispositionTokens();
  let checked = 0;
  for (const preview of PREVIEW_ANSWERS) {
    const { article_outcomes: rows } = preview.answer.members;
    assert.ok(rows.length > 0, `${preview.heading} shows no outcome`);
    const held = new Map(rows.map((row) => [row.disposition, row.outcomes]));
    for (const row of rows) {
      checked += 1;
      assert.ok(tokens.has(row.disposition), `${preview.heading}: "${row.disposition}" is not a corpus disposition token`);
    }
    // The platform's note says the articles the index holds are exactly its admitted and marker-only
    // outcomes, so a preview whose two disagree with its own totals teaches a relation the platform
    // does not keep. (The page itself does not do this sum: it is the platform's statement.)
    assert.equal(
      (held.get("akn_admitted") ?? 0) + (held.get("akn_marker_only_evidence") ?? 0),
      preview.answer.totals.articles,
      `${preview.heading}: the held outcomes are not the totals' articles`,
    );
    assert.deepEqual(
      rows.map((row) => row.disposition),
      rows.map((row) => row.disposition).toSorted(),
      `${preview.heading}: the tokens are not in ordinal order`,
    );
  }
  assert.ok(checked > 0, "no preview outcome was checked; the vocabulary held nothing");

  // And the captured real answer keeps the relation the note states, which is the reason the
  // preview may teach it.
  const captured = await capturedAnswer();
  const capturedHeld = Object.fromEntries(
    captured.members.article_outcomes.map((row) => [row.disposition, row.outcomes]));
  assert.equal(
    (capturedHeld.akn_admitted ?? 0) + (capturedHeld.akn_marker_only_evidence ?? 0),
    captured.totals.articles,
  );
  for (const row of captured.members.article_outcomes) {
    assert.ok(tokens.has(row.disposition), `the capture carries "${row.disposition}", not a corpus token`);
  }
});

test("a mount that serves every registered operation says none where the unrouted ones would be listed", async () => {
  // N3, found by mutation on #719: the string renderer printed `none` here and a blank would have passed
  // all 764 tests. The branch is unreached while sixteen operations have no route, and it is reached the
  // day the last one is served, on a page whose rule is that an absence is a sentence and never a blank
  // cell. `Coverage.jsx` carries its own copy of the literal, so both renderers are held.
  const captured = withDigests(await capturedAnswer());
  const every = mutate(captured, (a) => {
    a.operations.served_operations = [
      ...a.operations.served_operations, ...a.operations.not_served_operations,
    ].toSorted();
    a.operations.not_served_operations = [];
  });
  assert.equal(
    every.operations.served_operations.length, every.operations.registered,
    "the fixture must serve every registered operation for this to mean anything");
  for (const [renderer, html] of [["string", string(every)], ["react", react(every)]]) {
    assert.equal(
      rows(html).get("registered, with no route on this mount"), "none",
      `${renderer} left the empty list blank`);
  }
});
