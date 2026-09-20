// The provenance page, against the answer the platform actually sends.
//
// The page this replaced was written from a live V2 tool's payload and REQUIRED a signature,
// printing "stamp signature valid: yes" beside a record digest. The V3 answer carries a fixed row
// saying no signature is held or made. So the page had to be rebuilt, and the question was what to
// build it against: a fixture written by hand is how the refusal catalogue came to teach a URL
// grammar no producer emits, and doing that again here would be repeating a defect I raised
// against my own work three slices ago.
//
// So the answer comes from `schemas/v3-platform/answer-samples.json`, captured by driving the real
// handler. Two things about that file shape this test:
//
//   * it NORMALISES the values the fixture re-mints per run -- the object reference and the mount
//     digests -- to a placeholder. So it is a reference for the SHAPE and the stable values, not a
//     renderable answer: `withDigests` fills exactly those fields and nothing else, and a field
//     that stopped being normalised would arrive as a real digest and pass through untouched.
//   * it is captured with `lu-legilux` coordinates, and the preview site promises every value on
//     it is synthetic. So the preview keeps synthetic values and is held to the captured SHAPE
//     instead -- the bridge that stops a hand-written preview drifting into a form nothing emits.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import {
  NOT_STATED,
  PROFILE_NOTE,
  narrowedNote,
  provenancePageName,
  readProvenance,
  renderProvenance,
} from "../scripts/provenance.mjs";
import { PREVIEW_ANSWERS, provenancePreviewPages } from "../scripts/provenance-preview.mjs";
import { Provenance } from "../.react-build/app.mjs";

const SAMPLES = new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url);
const PLACEHOLDER = "<varies-per-run>";

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
  const row = parsed.sampled?.find((sample) => sample.operation === "provenance");
  assert.ok(row, "the census holds no provenance answer; this test has nothing to hold the page to");
  assert.equal(row.object_type, "provenance_chain");
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

/**
 * The rendered text, with the entities both renderers use decoded.
 *
 * The platform's sentences carry apostrophes, and the two renderers escape them differently --
 * `&#39;` from this surface's own escaper, `&#x27;` from React. Asserting on raw markup would
 * therefore compare a sentence against a spelling of itself, and would pass or fail for a reason
 * that has nothing to do with whether the page said the thing.
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

/**
 * The rows a rendered page shows, as {label: visible text}.
 *
 * Both renderers write their own layout, so this reads the markup rather than the view: the string
 * renderer emits `<th scope="row">label</th><td>value</td>` and the React port `<dt>label</dt>
 * <dd>value</dd>`. Comparing the two maps is the only thing that catches a row present in one and
 * absent in the other, and comparing a map against the answer is the only thing that catches a row
 * showing the RIGHT value in the WRONG place.
 */
function rows(html) {
  const found = new Map();
  const pattern = /<(?:th scope="row"|dt)>(.*?)<\/(?:th|dt)>\s*<(?:td|dd)>(.*?)<\/(?:td|dd)>/gs;
  for (const [, label, value] of html.matchAll(pattern)) {
    const key = text(label).trim();
    const shown = text(value.replace(/<[^>]*>/g, " ")).replace(/\s+/g, " ").trim();
    found.set(key, found.has(key) ? `${found.get(key)} | ${shown}` : shown);
  }
  return found;
}

/**
 * The coarse FORM of a value, so a preview can hold its own synthetic values and still be caught
 * teaching a grammar no producer speaks.
 *
 * This is #703's lesson applied to this page and it had to be applied twice, because the bridge
 * below first compared field NAMES only and passed a preview whose permalink truncated the state
 * digest to eight characters and whose outcome was a token the corpus has never emitted.
 */
function form(value) {
  if (value === null) return "null";
  if (typeof value === "number") return "number";
  if (typeof value === "boolean") return "boolean";
  if (typeof value !== "string") return typeof value;
  if (/^[0-9a-f]{64}$/.test(value)) return "digest";
  if (value.startsWith("/")) {
    const [path, pinned] = value.split("--");
    return `path:${path.split("/").length - 1}${pinned === undefined ? "" : `--${form(pinned)}`}`;
  }
  if (/^[a-z][a-z0-9_]*$/.test(value)) return "token";
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return "date";
  if (/^https?:\/\//.test(value)) return "url";
  return "text";
}

/**
 * The paths the platform's own contract says may arrive null, and nowhere else.
 *
 * `sources_note` states it for the three body fields: "each null where the corpus holds none".
 * `rights_disposition` is null where the corpus records none, and `requested_language` is null when
 * no language was asked for. A preview showing null at one of these is showing a real shape; one
 * showing null anywhere else is showing a shape nothing sends.
 */
const NULLABLE = new Set([
  "requested_language",
  "states[].sources[].body_sha256",
  "states[].sources[].body_byte_length",
  "states[].sources[].body_receipt_sha256",
  "states[].sources[].rights_disposition",
]);

/** Every leaf of an answer, as path -> form, so two answers can be compared by grammar. */
function forms(node, prefix = "", found = new Map()) {
  if (Array.isArray(node)) {
    for (const item of node) forms(item, `${prefix}[]`, found);
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const path = prefix.length === 0 ? key : `${prefix}.${key}`;
      if (value !== null && typeof value === "object") forms(value, path, found);
      else found.set(path, form(value));
    }
  }
  return found;
}

/** Every field path an answer carries, so two answers can be compared by shape and not by value. */
function paths(node, prefix = "", found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) paths(item, `${prefix}[]`, found);
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      const path = prefix.length === 0 ? key : `${prefix}.${key}`;
      found.add(path);
      paths(value, path, found);
    }
  }
  return found;
}

test("the page renders the answer the platform really sends", async () => {
  const answer = withDigests(await capturedAnswer());
  const html = renderProvenance(answer);

  for (const state of answer.states) {
    assert.ok(html.includes(state.state_sha256), "a state digest is missing from the page");
    assert.ok(html.includes(state.permalink));
    assert.ok(html.includes(state.stable_coordinate));
    assert.ok(html.includes(state.article_identities_sha256));
  }

  // The three sentences, verbatim. They are the platform's words about its own limits, and a page
  // that paraphrased them would put this service's voice on the platform's disclaimer.
  assert.ok(text(html).includes(answer.scope));
  assert.ok(text(html).includes(answer.derivation));
  assert.ok(text(html).includes(answer.sources_note));
  assert.ok(text(html).includes(PROFILE_NOTE));

  assert.ok(html.includes(answer.verified_by.corpus_sha256));
  assert.ok(html.includes(answer.verified_by.index_sha256));
  assert.ok(html.includes(answer.verified_by.registry_sha256));
});

test("every row of what the answer does not hold is rendered, with its reason", async () => {
  const answer = withDigests(await capturedAnswer());
  const html = renderProvenance(answer);

  assert.ok(answer.not_held.length > 0, "the captured answer declares nothing absent");
  for (const row of answer.not_held) {
    assert.ok(html.includes(row.item), `${row.item} is not on the page`);
    assert.ok(text(html).includes(row.reason), `${row.item} is named with no reason`);
  }

  // A row that disappears takes a reader's chance to notice it was ever expected.
  assert.throws(
    () => renderProvenance({ ...answer, not_held: [] }),
    /would read as an answer that holds everything/,
  );
  assert.throws(
    () => renderProvenance({ ...answer, not_held: [{ item: "first_sighting_event" }] }),
    /not_held\[0\]\.reason/,
  );
});

test("the page never claims a signature, and refuses the payload that carried one", async () => {
  const answer = withDigests(await capturedAnswer());
  const html = renderProvenance(answer);

  // The platform says it signs nothing. The page this replaced printed "stamp signature valid:
  // yes", so the absence of that claim is a property to hold rather than assume.
  assert.ok(!/signature[_ ]valid/i.test(html), "the page printed a signature validity claim");
  assert.ok(text(html).includes("signs nothing"), "the platform's own sentence about signing is missing");

  // And the old contract is an error rather than a page quietly missing its sections: a reader
  // cannot tell a platform that holds no signature from a page that forgot to print one.
  for (const member of ["stamp", "events", "observations", "document"]) {
    assert.throws(
      () => renderProvenance({ ...answer, [member]: {} }),
      /belongs to the tool this page was written against before V3/,
      `${member} was accepted`,
    );
  }
});

test("a value the platform does not hold is said out loud, never left blank", async () => {
  const answer = withDigests(await capturedAnswer());
  const state = answer.states[0];
  const withoutBody = {
    ...answer,
    states: [
      {
        ...state,
        sources: [
          {
            ...state.sources[0],
            body_sha256: null,
            body_byte_length: null,
            body_receipt_sha256: null,
            rights_disposition: null,
          },
        ],
      },
    ],
  };

  const html = renderProvenance(withoutBody);
  // Four nulls, four sentences. A blank cell reads as a fact about the publisher's document; this
  // is a fact about what this corpus retained, and the two are not the same claim.
  assert.equal((html.match(new RegExp(NOT_STATED, "g")) ?? []).length, 4);

  // But a value that is neither a digest nor null is a producer error, not an absence.
  assert.throws(
    () => renderProvenance({
      ...withoutBody,
      states: [{ ...state, sources: [{ ...state.sources[0], body_sha256: "nope" }] }],
    }),
    /is not a SHA-256 digest/,
  );
});

test("the preview teaches the shape the platform sends, with values of its own", async () => {
  // The bridge. The preview's values are synthetic because the site promises they are; its SHAPE
  // has to be the captured one, or this page teaches a form no producer emits -- which is exactly
  // what the refusal catalogue did until a guard was written for it.
  const captured = paths(await capturedAnswer());
  for (const preview of PREVIEW_ANSWERS) {
    const shown = paths(preview.answer);
    const missing = [...captured].filter((path) => !shown.has(path));
    const invented = [...shown].filter((path) => !captured.has(path));
    assert.deepEqual(missing, [], `${preview.lexId} omits ${missing.join(", ")}`);
    assert.deepEqual(
      invented, [], `${preview.lexId} carries ${invented.join(", ")}, which no producer sends`);
  }
});

test("both renderers apply the same rules and refuse the same answers", async () => {
  const answer = withDigests(await capturedAnswer());
  const react = renderToStaticMarkup(h(Provenance, { answer }));
  const string = renderProvenance(answer);

  for (const [name, html] of [["react", react], ["string", string]]) {
    assert.ok(text(html).includes(answer.scope), `${name} dropped the scope sentence`);
    assert.ok(text(html).includes(answer.derivation), `${name} dropped the derivation`);
    assert.ok(!/signature[_ ]valid/i.test(html), `${name} claimed a signature`);
    for (const row of answer.not_held) {
      assert.ok(text(html).includes(row.reason), `${name} dropped the reason for ${row.item}`);
    }
  }

  // The rule lives once, so both refuse together. If one renderer kept its own copy this is the
  // assertion that would catch it, and the dossier is why it is written.
  const old = { ...answer, stamp: { signature_valid: true } };
  assert.throws(() => renderToStaticMarkup(h(Provenance, { answer: old })), /before V3/);
  assert.throws(() => renderProvenance(old), /before V3/);
});

test("a provenance page is named after the state it describes, or refused", () => {
  assert.equal(
    provenancePageName("preview-synthetic:synthetic-preview-work:2001-01-01"),
    "provenance-preview-synthetic~synthetic-preview-work~2001-01-01.html",
  );
  for (const bad of ["", "has space", "has/slash", "has%3Aescape"]) {
    assert.throws(() => provenancePageName(bad), /cannot be named without colliding/);
  }
});

test("the preview builds one page per state and the harness can still find them", () => {
  const pages = provenancePreviewPages();
  assert.equal(pages.length, PREVIEW_ANSWERS.length);
  const names = pages.map(([name]) => name);
  // The evidence harness names this page by hand; if the preview stops emitting it the harness
  // measures a page that is not there and says nothing about the one that is.
  assert.ok(names.includes("provenance-preview-synthetic~synthetic-preview-work~2001-01-01.html"));
  assert.equal(new Set(names).size, names.length, "two previews share a page name");
  for (const [, html] of pages) {
    assert.ok(html.includes("Every value on this page is synthetic and none of it is law."));
  }
});

test("readProvenance returns the decision and computes nothing twice", async () => {
  const answer = withDigests(await capturedAnswer());
  const view = readProvenance(answer);
  assert.equal(view.states.length, answer.states.length);
  assert.equal(view.notHeld.length, answer.not_held.length);
  assert.equal(view.verifiedBy.registry_sha256, answer.verified_by.registry_sha256);
  assert.equal(view.requestedLanguage, answer.requested_language);
});

test("each row shows its own field, in both renderers, and the two agree", async () => {
  // The writer seat's F2, and the worst of the four because the comment above `withDigests` already
  // CLAIMED this: a distinct digest per field "so a page printing one field's value in another
  // field's row would be caught rather than looking right". Nothing read a row. Swapping the corpus
  // and index digests between their rows passed all 766 tests, on a page whose whole subject is
  // which digest is which.
  const answer = withDigests(await capturedAnswer());
  const state = answer.states[0];
  const source = state.sources[0];
  const expected = new Map([
    ["publisher", answer.publisher],
    ["work", answer.work_key],
    ["identifier asked for", answer.requested_identifier],
    ["date asked for", answer.requested_date],
    ["languages held", answer.available_languages.join(" ")],
    ["state digest", state.state_sha256],
    ["permalink", state.permalink],
    ["stable coordinate", state.stable_coordinate],
    ["expression", state.expression_iri],
    ["publisher work", state.publisher_work_iri],
    ["publisher legal resource", state.publisher_legal_resource_iri],
    ["articles", String(state.articles)],
    ["article identities digest", state.article_identities_sha256],
    ["rule profiles", state.rule_profile_sha256s.join(" ")],
    ["object reference", source.object_ref_sha256],
    ["body digest", source.body_sha256],
    ["body bytes", String(source.body_byte_length)],
    ["body receipt", source.body_receipt_sha256],
    ["outcome", source.outcome],
    ["rights disposition", source.rights_disposition],
    ["gaps recorded", source.gaps.length === 0 ? "none recorded" : source.gaps.join(" ")],
    ["corpus", answer.verified_by.corpus_sha256],
    ["index", answer.verified_by.index_sha256],
    ["operation registry", answer.verified_by.registry_sha256],
  ]);

  const string = rows(renderProvenance(answer));
  const react = rows(renderToStaticMarkup(h(Provenance, { answer })));

  for (const [label, value] of expected) {
    assert.equal(string.get(label), value, `the string renderer's "${label}" row`);
    assert.equal(react.get(label), value, `the React port's "${label}" row`);
  }

  // And the two layouts are written twice, so a row present in one and absent in the other is the
  // drift only this comparison can see.
  assert.deepEqual(
    [...string.keys()].sort(),
    [...react.keys()].sort(),
    "the two renderers show different rows",
  );
});

test("every leaf the platform sends reaches the page", async () => {
  // The reader-side twin of the producer's property pin, and the writer seat's suggestion: the day
  // the census gains a member this page does not render, this fails instead of the member being
  // quietly dropped.
  const answer = withDigests(await capturedAnswer());
  const html = text(renderProvenance(answer));
  const leaves = [];
  const walk = (node) => {
    if (Array.isArray(node)) node.forEach(walk);
    else if (node && typeof node === "object") Object.values(node).forEach(walk);
    else if (node !== null && String(node).length > 0) leaves.push(String(node));
  };
  walk(answer);
  const missing = [...new Set(leaves)].filter((leaf) => !html.includes(leaf));
  assert.deepEqual(missing, [], `the page does not show ${missing.join(", ")}`);
});

test("the preview teaches the forms the platform sends, not only its field names", async () => {
  // The writer seat's F1. The bridge compared field NAMES and nothing else, so the preview passed
  // while its permalink truncated the state digest to eight characters and its outcome was a token
  // the corpus has never emitted. My own words on #703: a name is right and a value can still be in
  // a grammar no producer speaks.
  const captured = forms(await capturedAnswer());
  for (const preview of PREVIEW_ANSWERS) {
    const shown = forms(preview.answer);
    for (const [path, expected] of captured) {
      // The census normalises the per-run digests to a placeholder, which has no form of its own;
      // those paths are held by the name comparison and by the fill, not here.
      if (expected === "text" && path.endsWith("sha256")) continue;
      // A preview may show null exactly where the platform's own sentence says null arrives -- the
      // sources note: "each null where the corpus holds none" -- and one preview exists to show
      // precisely that case. Anywhere else, null is a form the platform does not send there.
      if (NULLABLE.has(path) && shown.get(path) === "null") continue;
      assert.equal(
        shown.get(path),
        expected,
        `${preview.lexId}: ${path} is ${shown.get(path)} and the platform sends ${expected}`,
      );
    }
  }
});

test("the platform's free text is escaped, in both renderers", async () => {
  // The writer seat's F3, and the same gap as the dossier's F2 three slices ago: values the platform
  // sends, printed unescaped, with nothing to say so. Every free-text field the page prints.
  const answer = withDigests(await capturedAnswer());
  const hostile = "<img src=x onerror=alert(1)> & more";
  const cases = [
    ["scope", { ...answer, scope: hostile }],
    ["derivation", { ...answer, derivation: hostile }],
    ["sources_note", { ...answer, sources_note: hostile }],
    ["not_held reason", { ...answer, not_held: [{ item: "first_sighting_event", reason: hostile }] }],
    ["state language", {
      ...answer,
      states: [{ ...answer.states[0], language: hostile }],
    }],
    ["outcome", {
      ...answer,
      states: [{
        ...answer.states[0],
        sources: [{ ...answer.states[0].sources[0], outcome: hostile }],
      }],
    }],
  ];

  for (const [name, props] of cases) {
    for (const [renderer, html] of [
      ["string", renderProvenance(props)],
      ["react", renderToStaticMarkup(h(Provenance, { answer: props }))],
    ]) {
      assert.equal(html.includes("<img"), false, `${renderer} let ${name} through as markup`);
      assert.equal(text(html).includes(hostile), true, `${renderer} did not print ${name} as text`);
    }
  }
});

test("a language-narrowed answer says so, and an unnarrowed one does not", async () => {
  // The writer seat's F4. `narrowedNote` was imported by the React port and asserted nowhere;
  // deleting it from either renderer failed nothing. It is the sentence that stops a narrowed
  // answer being read as the whole record.
  const answer = withDigests(await capturedAnswer());
  const narrowed = { ...answer, requested_language: "fra" };
  const whole = { ...answer, requested_language: null };

  for (const [renderer, render] of [
    ["string", (props) => renderProvenance(props)],
    ["react", (props) => renderToStaticMarkup(h(Provenance, { answer: props }))],
  ]) {
    assert.equal(
      text(render(narrowed)).includes(narrowedNote("fra")),
      true,
      `${renderer} did not say the answer was narrowed`,
    );
    assert.equal(
      text(render(whole)).includes("was narrowed to"),
      false,
      `${renderer} said an unnarrowed answer was narrowed`,
    );
  }
});
