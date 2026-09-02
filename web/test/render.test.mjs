// Induced mutations for the envelope-free states.
//
// These three pages are the ones most likely to be got wrong quietly, because none
// of them has an envelope to render and each is therefore a page about nothing. The
// risk is not a broken page; it is a page that implies a legal fact it does not have.

import assert from "node:assert/strict";
import test from "node:test";

import {
  page,
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

import { readFileSync } from "node:fs";
import { loadCaptured } from "../scripts/captured-envelopes.mjs";
import { decodeEnvelope, validateEnvelope } from "../scripts/envelope.mjs";

const json = (path) => JSON.parse(readFileSync(new URL(path, import.meta.url), "utf8"));
const ENVELOPE_SCHEMA = json("../../schemas/v3-synthetic-preview/synthetic-resolve-envelope.schema.json");
const REGISTRY = new Map([
  ["lex-v3-preview-object-set/1", json("../../schemas/v3-preview/preview-object-set.schema.json")],
]);

// The fixtures are the envelopes the merged C# production path actually produced. Tests
// that built their own envelope would only ever prove the renderer agrees with my idea
// of the contract, which is exactly the mistake these tests exist to catch: the first
// version of this suite was written against the wrong schema entirely, and every test
// passed.
const capture = (file) => {
  const raw = loadCaptured(file);
  const { decoded, problems } = decodeEnvelope(ENVELOPE_SCHEMA, raw, REGISTRY);
  assert.deepEqual(problems, [], `${file} must decode cleanly`);
  assert.deepEqual(
    validateEnvelope(ENVELOPE_SCHEMA, decoded, REGISTRY),
    [],
    `${file} must validate`,
  );
  return decoded;
};

const SUCCESS = () => renderSuccess({ envelope: capture("success.json") });
const REFUSAL = (over = {}) => {
  const envelope = capture("refusal.json");
  return renderRefusal({ envelope: { ...envelope, refusal: { ...envelope.refusal, ...over } } });
};

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

// The wire format encodes closed vocabularies as integer indices. Rendering one raw puts
// a bare number where a reader expects a machine code, and that is an answer-shaped
// thing that says nothing.
// This assertion used to look only inside <code> elements, and the page leaked
// `<li>0</li>` straight past it. Codex found it in the built HTML. The check is now over
// every text node in the body: no element's entire text may be a bare integer, because
// on these pages there is no legitimate one. That is the assertion that would have
// caught the tuple-schema gap, and the narrower one is why it did not.
test("no element renders a bare integer, so no vocabulary index leaks", () => {
  for (const [name, html] of [["success", SUCCESS()], ["refusal", REFUSAL()]]) {
    const body = html.slice(html.indexOf("<main"));
    const bare = [...body.matchAll(/>\s*(-?[0-9]+)\s*</g)].map((m) => m[1]);
    assert.deepEqual(bare, [], `${name} rendered ${bare.length} bare integer(s): ${bare.join(", ")}`);
  }
});

// The tuple positions specifically. `publisher_contexts_checked` is a prefixItems tuple
// closed with `items: false`, and a decoder that reads only `items` skips it entirely.
test("a prefixItems tuple decodes through its declared vocabulary", () => {
  const html = REFUSAL();
  assert.ok(html.includes("<li>lu-legilux</li>"), "the tuple member must render as its name");

  const envelope = capture("refusal.json");
  assert.equal(envelope.refusal.publisher_contexts_checked[0], "lu-legilux");

  // The tail is closed, so an extra member has no schema and must be refused rather
  // than passed through undecoded.
  const extra = loadCaptured("refusal.json");
  extra.refusal.publisher_contexts_checked = [0, 0];
  const { problems } = decodeEnvelope(ENVELOPE_SCHEMA, extra, REGISTRY);
  assert.ok(problems.length > 0, "an over-length closed tuple must be refused");
});

// The closed tail on its own. In the real schema `maxItems` and `items: false` are
// coextensive, so the length check alone rejects an extra member and the tail branch is
// unreachable there. Deleting that branch left the suite green, which is the same shape
// as every other weak assertion found today: one guard exercised only through a case a
// second guard also rejects. This schema exists to isolate it.
test("a closed tail refuses a member no schema describes", () => {
  const closedTail = {
    anyOf: [
      {
        properties: {
          branch: { const: "refusal" },
          refusal: {
            type: "object",
            properties: {
              families: {
                type: "array",
                items: false,
                prefixItems: [{ enum: ["eli", "celex"] }],
              },
            },
          },
        },
      },
    ],
  };
  const envelope = (families) => ({ branch: "refusal", refusal: { families } });

  const good = decodeEnvelope(closedTail, envelope([1]), new Map());
  assert.deepEqual(good.problems, [], "a member the tuple describes decodes cleanly");
  assert.deepEqual(good.decoded.refusal.families, ["celex"]);

  const extra = decodeEnvelope(closedTail, envelope([1, 0]), new Map());
  assert.ok(
    extra.problems.some((p) => p.includes("beyond the closed tuple")),
    "a member past the closed tail must be refused by the tail rule itself",
  );
});

test("vocabulary indices are resolved to their declared members", () => {
  const refusal = REFUSAL();
  assert.ok(refusal.includes(">identifier_unknown<"), "refusal.code must render as its name");
  assert.ok(refusal.includes("historical_legal_id"), "checked_identifier_family must resolve");
  assert.ok(refusal.includes("corrected_identifier"), "what_would_answer must resolve");

  const success = SUCCESS();
  assert.ok(success.includes("held_public"), "body_holding_state resolves via the nested schema");
  assert.ok(success.includes("synthetic_fixture"), "body_holding_disposition resolves too");
  assert.ok(success.includes("eli"), "matched_identifier_family must resolve");
});

test("a refusal is rendered as an answer, never as an error", () => {
  const html = REFUSAL();
  assert.ok(html.includes('data-preview-state="refusal"'));
  assert.ok(html.includes('class="code-chip">identifier_unknown<'));
  assert.ok(!/class="[^"]*(error|danger|red)/.test(html));
});

test("a refusal that asserts no absence says so explicitly", () => {
  assert.ok(REFUSAL().includes("does <strong>not</strong> assert"));
  assert.ok(REFUSAL({ asserts_absence_of_law: true }).includes("asserts the absence of a law"));
});

test("empty payload lists are omitted rather than rendered empty", () => {
  assert.ok(!REFUSAL({ possible_held_records: [] }).includes("Records that may be held"));
  assert.ok(REFUSAL().includes("Records that may be held"));
});

// A prefix check on "https://" is not enough, and I shipped one. These are the shapes
// that got through it, kept as tests so the parse-based guard cannot regress to a
// pattern match: userinfo that displays a trustworthy host and navigates elsewhere, an
// arbitrary host, an explicit port, a cross-publisher host, and an unknown publisher.
test("a route is linked only when its host belongs to the named publisher", () => {
  const route = (uri, publisher = "lu-legilux") =>
    REFUSAL({ official_search_actions: [{ kind: "publisher_search", publisher, uri }] });
  const linked = (html) => /<a href="([^"]*)"/.exec(html)?.[1] ?? null;

  assert.equal(
    linked(route("https://legilux.public.lu/search")),
    "https://legilux.public.lu/search",
    "the official route must be linked",
  );

  for (const [label, uri, publisher] of [
    ["userinfo pointing elsewhere", "https://legilux.public.lu@evil.invalid/x", "lu-legilux"],
    // Userinfo on an *allowed* host, so the allowlist cannot be what rejects it. Without
    // this case, deleting the userinfo check left the suite green.
    ["userinfo on an allowed host", "https://user:pass@legilux.public.lu/search", "lu-legilux"],
    ["an arbitrary host", "https://evil.invalid/#legilux.public.lu", "lu-legilux"],
    ["an explicit port", "https://legilux.public.lu:8443/search", "lu-legilux"],
    ["a non-https scheme", "http://legilux.public.lu/search", "lu-legilux"],
    ["a scheme that executes", "javascript:alert(1)", "lu-legilux"],
    ["a host belonging to another publisher", "https://legilux.public.lu/search", "eu-eurlex"],
    ["a publisher with no declared hosts", "https://legilux.public.lu/search", "unknown-publisher"],
    ["an unparseable value", "not a url at all", "lu-legilux"],
  ]) {
    const html = route(uri, publisher);
    assert.equal(linked(html), null, `${label} must not be linked`);
    assert.ok(html.includes("not an official host"), `${label} must say why`);
  }

  assert.ok(
    linked(route("https://eur-lex.europa.eu/search", "eu-eurlex")),
    "each publisher's own hosts are allowed",
  );
});

// A route that needs repairing before it is safe is not repaired, it is refused. The
// previous rule emitted the parsed URL, which meant `https:///legilux.public.lu/search`, a
// string with no written authority at all, became a link to Legilux. Validating the raw
// authority first means the written form and the destination are the same thing, and a
// string that is not already a plain host never becomes a link.
test("a route whose written authority is not a host is refused, never repaired", () => {
  for (const uri of [
    "https:///legilux.public.lu/search",
    "https://LEGILUX.public.lu/search",
    "https://legilux.public.lu:443/search",
    "https://@legilux.public.lu/search",
  ]) {
    const html = REFUSAL({
      official_search_actions: [{ kind: "publisher_search", publisher: "lu-legilux", uri }],
    });
    assert.equal(/<a href="([^"]*)"/.exec(html)?.[1], undefined, `${uri} became a link`);
  }

  const good = REFUSAL({
    official_search_actions: [
      { kind: "publisher_search", publisher: "lu-legilux", uri: "https://legilux.public.lu/search" },
    ],
  });
  assert.equal(/<a href="([^"]*)"/.exec(good)?.[1], "https://legilux.public.lu/search");
});

test("the success page renders the object body and invents no legal time", () => {
  const html = SUCCESS();
  assert.ok(html.includes("s0-05-sql-object-set"));
  assert.ok(html.includes("SYNTHETIC PREVIEW"), "the object body is rendered");
  assert.ok(!/in force|valid_from|timeline_semantics/i.test(html));
});

test("a provenance row absent from the envelope is omitted, never defaulted", () => {
  const envelope = capture("success.json");
  const without = { ...envelope, context: { ...envelope.context, artifact: undefined } };
  assert.ok(!renderSuccess({ envelope: without }).includes("Artifact digest"));
  assert.ok(SUCCESS().includes("Artifact digest"));
});

// The fail-closed guarantee. An index that resolves to the wrong member is a confident
// wrong label, so anything outside the declared vocabulary is refused rather than
// clamped or passed through. Without these, disabling the range check leaves the whole
// suite green, which is how it was found.
test("a vocabulary index outside its declared members is refused", () => {
  const base = () => loadCaptured("refusal.json");
  const cases = [
    ["code past the only entry", (e) => { e.refusal.code = 1; }],
    ["family index equal to the member count", (e) => { e.refusal.checked_identifier_family = 4; }],
    ["negative index", (e) => { e.refusal.checked_identifier_family = -1; }],
    ["non-integer index", (e) => { e.refusal.checked_identifier_family = 1.5; }],
    ["index inside an array", (e) => { e.refusal.what_would_answer = [0, 9]; }],
    ["index nested in an object in an array", (e) => { e.refusal.possible_held_records[0].publisher = 2; }],
  ];
  for (const [label, mutate] of cases) {
    const envelope = base();
    mutate(envelope);
    const { problems } = decodeEnvelope(ENVELOPE_SCHEMA, envelope, REGISTRY);
    assert.ok(problems.length > 0, `${label} must be refused`);
    assert.ok(problems[0].includes("outside the"), `${label} must say why`);
  }

  const { problems } = decodeEnvelope(ENVELOPE_SCHEMA, base(), REGISTRY);
  assert.deepEqual(problems, [], "the unmutated capture must still decode cleanly");
});

test('asset references do not depend on how deep the page is served', () => {
  // Found by the browser journey gate, which followed a link to a nested route and landed on a
  // page whose stylesheet had 404ed. Every preview page sits at the root of dist/, where
  // "./styles.css" happens to resolve, so a flat preview can never show this. A real V3 route
  // is nested (/w/<work>/<version>, /provenance/<id>), and there "./styles.css" resolves inside
  // a directory that holds no stylesheet: the page renders completely unstyled.
  const html = page({
    state: 'dossier',
    title: 'Depth',
    locale: 'en',
    copyLocale: 'en',
    shell: 'w',
    density: 'reading',
    main: '      <h1>Depth</h1>\n',
  });
  for (const [, href] of html.matchAll(/<link[^>]+href="([^"]+)"/g)) {
    assert.equal(
      href.startsWith('/'),
      true,
      `${href} is resolved against the page's own path, so the same page served at a nested ` +
        'route loads a different file or none',
    );
  }
});
