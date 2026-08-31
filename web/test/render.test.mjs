// Induced mutations for the envelope-free states.
//
// These three pages are the ones most likely to be got wrong quietly, because none
// of them has an envelope to render and each is therefore a page about nothing. The
// risk is not a broken page; it is a page that implies a legal fact it does not have.

import assert from "node:assert/strict";
import test from "node:test";

import {
  escapeHtml,
  renderLoading,
  renderTransportFailure,
  renderInvalidEnvelope,
  SYNTHETIC_MARKER_VALUE,
} from "../scripts/render.mjs";

const ALL = () => [
  renderLoading(),
  renderTransportFailure({ reason: "the connection closed before a response arrived" }),
  renderInvalidEnvelope({ problems: ["context: missing required member snapshot"] }),
];

test("every generated page carries the machine-readable synthetic marker", () => {
  for (const html of ALL()) {
    assert.match(html, new RegExp(`data-synthetic="${SYNTHETIC_MARKER_VALUE}"`));
  }
});

test("every generated page states it is not law", () => {
  for (const html of ALL()) {
    assert.match(html, /not law/);
    assert.match(html, /not promotable/);
  }
});

test("each page declares its own state in the markup", () => {
  assert.match(renderLoading(), /data-preview-state="loading"/);
  assert.match(renderTransportFailure({ reason: "x" }), /data-preview-state="transport_failure"/);
  assert.match(renderInvalidEnvelope({ problems: [] }), /data-preview-state="invalid_envelope"/);
});

test("a transport failure never implies the record is absent", () => {
  const html = renderTransportFailure({ reason: "dns lookup failed" });
  assert.match(html, /says nothing about whether the requested record\s+exists/);
  assert.doesNotMatch(html, /not found|no such|does not exist/i);
});

test("an invalid envelope renders its problems and no legal content", () => {
  const html = renderInvalidEnvelope({
    problems: ["result: missing required member object_set_id", "schema: expected const"],
  });
  assert.match(html, /missing required member object_set_id/);
  assert.match(html, /expected const/);
  assert.match(html, /says nothing about whether the requested record/);
});

test("a loading page shows no result-shaped content", () => {
  const html = renderLoading();
  // A skeleton that mimics a result teaches the reader to expect one.
  assert.doesNotMatch(html, /object_set|sha256|coordinate value|<table/i);
});

test("hostile text in a reason cannot break out of the document", () => {
  const html = renderTransportFailure({
    reason: '</main><script>alert("x")</script><main>',
  });
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /&lt;script&gt;/);
});

test("hostile text in a validator problem cannot break out either", () => {
  const html = renderInvalidEnvelope({ problems: ['<img src=x onerror="alert(1)">'] });
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /&lt;img/);
});

test("escaping covers every character that can change meaning", () => {
  assert.equal(escapeHtml(`<&>"'`), "&lt;&amp;&gt;&quot;&#39;");
});

test("the reason is rendered from the argument, not from a template literal", () => {
  // Presence-only rendering would pass a naive test. Two distinct inputs must
  // produce two distinct outputs, or the page is not derived from its data.
  const a = renderTransportFailure({ reason: "alpha-distinct-reason" });
  const b = renderTransportFailure({ reason: "beta-distinct-reason" });
  assert.match(a, /alpha-distinct-reason/);
  assert.match(b, /beta-distinct-reason/);
  assert.notEqual(a, b);
});

test("problems are rendered from the argument, not from a template literal", () => {
  const a = renderInvalidEnvelope({ problems: ["alpha-problem"] });
  const b = renderInvalidEnvelope({ problems: ["beta-problem", "second-problem"] });
  assert.match(a, /alpha-problem/);
  assert.match(b, /second-problem/);
  assert.notEqual(a, b);
});

// The two data-bearing states.
//
// These are the states that can lie. The envelope-free pages above can only fail by
// implying a fact they do not have; these can fail by rendering the wrong fact, or by
// rendering a fact the envelope never carried.

import { renderSuccess, renderRefusal } from "../scripts/render.mjs";

const CONTEXT = (over = {}) => ({
  jurisdiction: "synthetic",
  capabilities: "preview_mechanics_only",
  source: { source_kind: "synthetic_test" },
  freshness: { observed_at: "2026-08-31T07:00:00Z", upstream_health: "not_applicable_synthetic" },
  index_format: "lex-index/3",
  snapshot: { snapshot_sha256: "a".repeat(64) },
  artifact: { artifact_id: "art-1" },
  runtime: { source_sha256: "b".repeat(64) },
  builder: { source_sha256: "c".repeat(64) },
  operation: { operation_id: "resolve", catalog_sha256: "d".repeat(64) },
  refusal_registry: { sha256: "e".repeat(64) },
  request_ref: "req_0123456789abcdef0123456789abcdef",
  ...over,
});

const SUCCESS = (over = {}, context = CONTEXT()) =>
  renderSuccess({
    envelope: {
      result: { object_set_id: "set-one", object_set_sha256: "1".repeat(64), ...over },
      context,
    },
  });

const REFUSAL = (over = {}) =>
  renderRefusal({
    envelope: {
      refusal: {
        code: "identifier_unknown",
        requested_coordinate: "eli/synthetic-preview",
        checked_identifier_family: "eli",
        publisher_contexts_checked: ["lu-legilux"],
        possible_held_records: [],
        what_would_answer: ["a known ELI"],
        official_search_actions: ["https://legilux.public.lu"],
        asserts_absence_of_law: false,
        ...over,
      },
      context: CONTEXT(),
    },
  });

// Escaping is asserted on the function, not through a rendered page. Through the page
// each character's assertion silently depended on a neighbouring escape still working:
// removing only `<` left `&lt;script>alert(1)`, which does not contain `<script>alert`,
// so the test passed while the hole was real. Testing the five characters one at a time
// is the only form of this test that fails when it should.
test("every HTML-significant character is escaped independently", () => {
  for (const [raw, entity] of [
    ["&", "&amp;"],
    ["<", "&lt;"],
    [">", "&gt;"],
    ['"', "&quot;"],
    ["'", "&#39;"],
  ]) {
    assert.equal(escapeHtml(raw), entity, `${raw} must escape to ${entity}`);
  }
});

test("a refusal is rendered as an answer, never as an error", () => {
  const html = REFUSAL();
  assert.match(html, /data-preview-state="refusal"/);
  assert.match(html, /class="code-chip">identifier_unknown</);
  assert.doesNotMatch(html, /class="[^"]*(error|danger|red)/);
});

test("a refusal that asserts no absence says so explicitly", () => {
  assert.match(REFUSAL(), /does <strong>not<\/strong> assert/);
  assert.match(REFUSAL({ asserts_absence_of_law: true }), /asserts the absence of a law/);
});

test("empty payload lists are omitted rather than rendered empty", () => {
  assert.doesNotMatch(REFUSAL(), /Records that may be held/);
  assert.match(REFUSAL({ possible_held_records: ["one"] }), /Records that may be held/);
});

test("the success page derives its values and invents no legal time", () => {
  const html = SUCCESS();
  assert.match(html, /set-one/);
  assert.match(html, /preview_mechanics_only/);
  assert.doesNotMatch(html, /in force|valid_from|timeline_semantics/i);
});

// Presence-only rendering passes any single-fixture test. Two payloads that differ must
// produce output that differs, and neither may carry the other's values.
test("different envelopes produce different pages", () => {
  const one = SUCCESS();
  const two = SUCCESS(
    { object_set_id: "set-two", object_set_sha256: "2".repeat(64) },
    CONTEXT({ operation: { operation_id: "timeline", catalog_sha256: "9".repeat(64) } }),
  );
  assert.notEqual(one, two);
  assert.match(one, /resolve/);
  assert.doesNotMatch(one, /set-two/);
  assert.match(two, /timeline/);
  assert.doesNotMatch(two, /set-one/);

  const single = REFUSAL();
  const both = REFUSAL({ publisher_contexts_checked: ["eu-eurlex", "lu-legilux"] });
  assert.notEqual(single, both);
  assert.doesNotMatch(single, /eu-eurlex/);
  assert.match(both, /eu-eurlex/);
});

test("a provenance row absent from the envelope is omitted, never defaulted", () => {
  const html = SUCCESS({}, CONTEXT({ artifact: undefined }));
  assert.doesNotMatch(html, /Artifact/);
  assert.match(SUCCESS(), /Artifact/);
});
