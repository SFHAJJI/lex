// The print view of Reading (the specification of record's R62, its fifth item: "a clean print view of Reading (33 §7 rule
// 9)"; UX spec section 3: "print stylesheet strips chrome, keeps banner, hashes and citation strings"): the stylesheet's
// print rules, held to the reading page the server renders at a permalink.
//
// - The chrome goes on paper: the skip link, the menu and the languages, the forms and buttons, the pager and the contents,
//   the info controls, and the footer's menus; each rule names an element the page holds, so none is a rule for nothing.
// - What a reader cites stays: no print rule hides the text, a digest, a source, a permalink, a citation form, a version's
//   heading (its date by its role), the breadcrumbs, a banner, or the footer's lines (what Lex is not, which texts are
//   authentic, where the texts come from) and how current the answer is.
// - On paper the page is black on white in either colour scheme, and a long digest wraps inside the page.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";

import { renderLiveReadingPage, LIVE_CONTRACT } from "../.react-build/app.mjs";
import { readingOutcome } from "../scripts/live-reading.mjs";
import { answerHandedWith } from "../scripts/v3-client.mjs";

const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const PINNED = census.envelopes.find((entry) => entry.operation === "evidence_bundle" && entry.scenario.startsWith("a state's permalink at its own date")).envelope;

/** The body of the stylesheet's one `@media print` block, its braces matched. */
function printBlock(text) {
  const start = text.indexOf("@media print {");
  assert.ok(start >= 0, "the stylesheet has a print block");
  assert.equal(text.indexOf("@media print {", start + 1), -1, "one print block, so its rules are read in one place");
  let depth = 0;
  for (let at = text.indexOf("{", start); at < text.length; at += 1) {
    if (text[at] === "{") depth += 1;
    if (text[at] === "}") depth -= 1;
    if (depth === 0) return text.slice(text.indexOf("{", start) + 1, at);
  }
  throw new Error("the print block is not closed");
}

/** Each rule of a block as `{ selectors, body }`, comments taken out. */
function rules(block) {
  return [...block.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .map(([, selectors, body]) => ({ selectors: selectors.split(",").map((one) => one.trim()).filter(Boolean), body }));
}

const PRINT = rules(printBlock(css));
const hidden = PRINT.filter((rule) => /display:\s*none/.test(rule.body)).flatMap((rule) => rule.selectors);

/** The chrome the print view strips, each by its selector and a marker the rendered page holds for it. */
const STRIPPED = Object.freeze([
  [".skip-link", 'class="skip-link"'],
  [".site-nav", 'class="site-nav"'],
  [".site-header .locale-nav", "locale-nav"],
  ["main form", 'role="search"'],
  ["button", "<button"],
  [".site-footer .reference-nav", 'class="reference-nav"'],
  [".site-footer .legal-nav", 'class="legal-nav"'],
  [".info-control", 'class="info-control"'],
]);

/** What a reader cites, which no print rule may hide: each by a marker the rendered page holds for it. */
const KEPT = Object.freeze([
  ["blockquote", "<blockquote"],
  ["[data-quote-evidence]", "data-quote-evidence"],
  ["[data-citation-form]", "data-citation-form"],
  ["[data-date-role]", "data-date-role"],
  [".breadcrumbs", 'class="breadcrumbs"'],
  ["[data-not-advice]", "data-not-advice"],
  [".site-footer", 'class="site-footer"'],
  [".freshness-strip", 'class="freshness-strip"'],
  [".staleness-banner", null],
  ["[data-as-published]", null],
  ["[data-authenticity]", null],
  ["code", "<code"],
]);

test("on paper the reading page loses its chrome: every rule names an element the page the server renders holds", () => {
  const state = PINNED.result.value.states[0];
  const article = state.articles[0].publisher_id;
  const html = renderLiveReadingPage({ initial: { envelope: PINNED, path: `/en${state.permalink}/${article}` } });
  assert.equal(readingOutcome(answerHandedWith("evidence_bundle", PINNED, { contract: LIVE_CONTRACT, objectType: "evidence_bundle" })).state, "success");
  for (const [selector, marker] of STRIPPED) {
    assert.ok(hidden.includes(selector), `the print view hides ${selector}`);
    assert.ok(html.includes(marker), `the reading page holds ${selector} (${marker}), so its print rule is no rule for nothing`);
  }
});

test("on paper the text, its digests, sources, permalinks and citation forms, the dates by role, the banners and the attribution stay", () => {
  const state = PINNED.result.value.states[0];
  const html = renderLiveReadingPage({ initial: { envelope: PINNED, path: `/en${state.permalink}/${state.articles[0].publisher_id}` } });
  for (const [selector, marker] of KEPT) {
    assert.ok(!hidden.includes(selector), `the print view keeps ${selector}`);
    if (marker !== null) assert.ok(html.includes(marker), `the reading page holds ${selector}`);
  }
  // No hidden selector reaches what is kept by being broader: none is an element or attribute a citation stands in.
  for (const selector of hidden) assert.doesNotMatch(selector, /^(?:main|section|p|li|ol|dl|dd|dt|code|blockquote|h[1-6]|footer|\.site-footer|\.site-header|\[data-live-answer\])$/, selector);
});

test("on paper the page is black on white whatever the colour scheme, at its full width, and a long digest wraps", () => {
  const block = printBlock(css);
  assert.match(block, /:root,\s*body\s*\{[^}]*background:\s*#ffffff !important;[^}]*color:\s*#000000 !important;/);
  assert.match(block, /main,\s*\.site-header ~ main\s*\{[^}]*min-block-size:\s*auto;[^}]*max-inline-size:\s*none;/);
  assert.match(block, /code\s*\{[^}]*overflow-wrap:\s*anywhere;/);
  assert.match(block, /\[data-quote-evidence\],\s*\[data-citation-form\],\s*\.breadcrumbs\s*\{[^}]*break-inside:\s*avoid;/);
});

test("a touch target is at least 44 by 44 CSS pixels, links in lists, summaries, controls and the info control alike (R62)", () => {
  const rule = (selector) => {
    const at = css.indexOf(selector);
    assert.ok(at >= 0, selector);
    return css.slice(at, css.indexOf("}", at));
  };
  for (const selector of ["li > a,\nsummary,\n.verify-cluster a {", "input[type='date'] {", ".check-official {", ".info-control {"]) {
    assert.match(rule(selector), /min-block-size: 44px;/, selector);
  }
  for (const selector of ["li > a,\nsummary,\n.verify-cluster a {", "input[type='date'] {", ".info-control {"]) {
    assert.match(rule(selector), /min-inline-size: 44px;/, selector);
  }
  assert.doesNotMatch(css, /min-(?:block|inline)-size: 24px;/, "no target keeps the 24 pixel floor");
  // Every control stands in the controls' rule, the select and the checkbox too (the review of #1004, F5: a select in a form was
  // about 40 pixels tall), and nothing after it lowers a select's or a checkbox's block size.
  const head = css.indexOf("button,\n.filter-chip,");
  const controls = css.slice(head, css.indexOf("{", head)).split(",").map((selector) => selector.trim());
  for (const control of ["button", ".filter-chip", "select", "input[type='checkbox']", "input[type='text']", "input[type='search']", "input[type='date']"]) {
    assert.ok(controls.includes(control), control);
  }
  assert.match(css.slice(head, css.indexOf("}", head)), /min-block-size: 44px;/);
  assert.doesNotMatch(css, /(?:select|checkbox[^{]*)\{[^}]*(?:min-block-size|block-size|height):\s*(?:[0-3]?\d|4[0-3])px/, "no rule lowers a select or a checkbox");
});
