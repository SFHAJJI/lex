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
