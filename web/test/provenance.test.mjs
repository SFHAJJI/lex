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
