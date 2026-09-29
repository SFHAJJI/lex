import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

import { readV3Envelope } from "../scripts/v3-envelope.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const sample = (operation, state) => census.envelopes.find(
  (entry) => entry.operation === operation && (state === "refusal" ? entry.envelope.refusal !== null : entry.envelope.result !== null),
);
const clone = (value) => JSON.parse(JSON.stringify(value));

test("every envelope the platform sent reads, as the state it is", () => {
  assert.ok(census.envelopes.length >= 4, "the census holds the envelopes this reader is written against");
  for (const entry of census.envelopes) {
    const read = readV3Envelope(entry.envelope, contract, { operation: entry.operation });
    assert.equal(read.ok, true, `${entry.operation} / ${entry.scenario}: ${read.reason}`);
    assert.equal(read.state, entry.envelope.refusal === null ? "success" : "refusal", entry.scenario);
  }
  const ask = readV3Envelope(sample("ask", "success").envelope, contract, { operation: "ask", objectType: "handoff_card" });
  assert.equal(ask.envelope.verdict, "point", "the contained assistant's card is read under its own verdict");
});

test("the contract is the reviewed registry's, and the two censuses agree on its digest", () => {
  const coverage = answers.sampled.find((entry) => entry.operation === "coverage");
  assert.equal(contract.registry_sha256, coverage.answer.mounted.registry_sha256);
  assert.equal(contract.refusals.length, 20, "the closed registry of twenty refusal codes");
  assert.equal(contract.operations.length, 27, "every registered operation");
});

test("each departure from the platform's envelope is refused, with its own reason", () => {
  const success = sample("coverage", "success").envelope;
  const refusal = sample("coverage", "refusal").envelope;
  const withPayload = sample("search", "refusal").envelope;
  const cases = [
    ["an extra member", success, (e) => { e.extra = 1; }, /exact closed member set/],
    ["a missing member", success, (e) => { delete e.request_ref; }, /exact closed member set/],
    ["another registry", success, (e) => { e.registry_sha256 = "0".repeat(64); }, /reviewed registry/],
    ["another schema", success, (e) => { e.schema = "lex-v3-envelope/2"; }, /unexpected schema/],
    ["both branches", success, (e) => { e.refusal = clone(refusal.refusal); }, /exactly one of result and refusal/],
    ["neither branch", success, (e) => { e.result = null; }, /exactly one of result and refusal/],
    ["a result under refuse", success, (e) => { e.verdict = "refuse"; }, /never carries the refuse verdict/],
    ["an unknown verdict", success, (e) => { e.verdict = "maybe"; }, /unknown verdict/],
    ["a status that contradicts the branch", success, (e) => { e.context.status = "refusal"; }, /context status is success/],
    ["a jurisdiction that contradicts the publisher", success, (e) => { e.context.jurisdiction = "eu"; }, /jurisdiction does not match/],
    ["timeline semantics that contradict the publisher", success, (e) => { e.context.timeline_semantics = "official_consolidation_state"; }, /timeline semantics do not match/],
    ["a snapshot digest that is not one", success, (e) => { e.context.snapshot.snapshot_sha256 = "x"; }, /not a digest/],
    ["an observation time in another form", success, (e) => { e.context.freshness.observed_at = "2026-09-17"; }, /real instant/],
    ["an unknown upstream health", success, (e) => { e.context.freshness.upstream_health = "fine"; }, /unknown upstream health/],
    ["a result schema of another operation", success, (e) => { e.result.schema = "lex-v3-search-result/1"; }, /result schema is not bound/],
    ["an object type the operation does not declare", success, (e) => { e.result.object_type = "quote"; }, /object type is not bound/],
    ["a result value that is not an object", success, (e) => { e.result.value = []; }, /value is not an object/],
    ["a refusal under answer", refusal, (e) => { e.verdict = "answer"; }, /refuse verdict/],
    ["a code outside the registry", refusal, (e) => { e.refusal.code = "not_found"; }, /closed registry/],
    ["a payload missing a mandatory field", withPayload, (e) => { delete e.refusal.helpful_payload.available_languages; }, /missing available_languages/],
    ["an empty payload", refusal, (e) => { e.refusal.helpful_payload = {}; }, /helpful payload/],
    ["another refusal schema", refusal, (e) => { e.refusal.schema = "lex-v3-refusal/2"; }, /refusal schema/],
    // The rules the review of #763 found unheld: each is the platform's, and each is refused here.
    ["another version", success, (e) => { e.version = "v4"; }, /unexpected version/],
    ["another object type", success, (e) => { e.object_type = "answer"; }, /unexpected object_type/],
    ["another registry schema", success, (e) => { e.registry_schema = "lex-v3-operation-registry/2"; }, /unexpected registry_schema/],
    ["a request reference that is not opaque", success, (e) => { e.request_ref = "Not Opaque!"; }, /opaque bounded reference/],
    ["a request reference over 128 characters", success, (e) => { e.request_ref = "a".repeat(129); }, /opaque bounded reference/],
    ["an unregistered operation", success, (e) => { e.operation_id = "horoscope"; }, /not a registered one/],
    ["an unknown publisher", success, (e) => { e.context.publisher = "de-bgbl"; }, /unknown publisher/],
    ["a provisional flag that is not a boolean", success, (e) => { e.context.provisional = "no"; }, /provisional is not a boolean/],
    ["a blank snapshot identity", success, (e) => { e.context.snapshot.snapshot_id = "   "; }, /bounded printable identifier/],
    ["a snapshot identity outside printable ASCII", success, (e) => { e.context.snapshot.snapshot_id = "corpus-é"; }, /bounded printable identifier/],
    ["a snapshot identity over 256 characters", success, (e) => { e.context.snapshot.snapshot_id = "s".repeat(257); }, /bounded printable identifier/],
    ["a date the calendar does not have", success, (e) => { e.context.freshness.observed_at = "2026-02-30T00:00:00.0000000Z"; }, /real instant/],
    ["a time the clock does not have", success, (e) => { e.context.freshness.observed_at = "2026-09-17T25:61:61.0000000Z"; }, /real instant/],
    ["the default instant", success, (e) => { e.context.freshness.observed_at = "0001-01-01T00:00:00.0000000Z"; }, /real instant/],
    ["a context member too many", success, (e) => { e.context.extra = 1; }, /the context does not have the exact closed member set/],
    ["a snapshot member too many", success, (e) => { e.context.snapshot.extra = 1; }, /the snapshot does not have the exact closed member set/],
    ["a freshness member too many", success, (e) => { e.context.freshness.extra = 1; }, /the freshness does not have the exact closed member set/],
    ["a result member too many", success, (e) => { e.result.extra = 1; }, /the result does not have the exact closed member set/],
    ["a refusal member too many", refusal, (e) => { e.refusal.extra = 1; }, /the refusal does not have the exact closed member set/],
    ["a refusal whose status says success", refusal, (e) => { e.context.status = "success"; }, /refusal's context status is refusal/],
    ["a value nested past the platform's limit", success, (e) => {
      let node = {};
      e.result.value.deep = node;
      for (let index = 0; index < 40; index += 1) { node.next = {}; node = node.next; }
    }, /nests deeper than 32/],
  ];
  for (const [what, base, mutate, reason] of cases) {
    const envelope = clone(base);
    mutate(envelope);
    const read = readV3Envelope(envelope, contract, { operation: envelope.operation_id });
    assert.equal(read.ok, false, `${what} was read`);
    assert.match(read.reason, reason, what);
  }
});

test("an instant the platform can write is read, early years included", () => {
  const envelope = clone(sample("coverage", "success").envelope);
  envelope.context.freshness.observed_at = "0050-06-15T12:30:45.1234567Z";
  assert.equal(readV3Envelope(envelope, contract).ok, true, readV3Envelope(envelope, contract).reason);
  envelope.context.freshness.observed_at = "2024-02-29T23:59:59.9999999Z";
  assert.equal(readV3Envelope(envelope, contract).ok, true, "a leap day");
});

test("the answer must be to the question asked", () => {
  const success = sample("coverage", "success").envelope;
  assert.match(readV3Envelope(success, contract, { operation: "search" }).reason, /answers coverage, not search/);
  assert.match(readV3Envelope(success, contract, { operation: "coverage", objectType: "quote" }).reason, /not a quote/);
  assert.match(readV3Envelope(success, null).reason, /no contract/);
});

test("anchor_not_in_version names its nearest anchors and forbids the fallback, as the platform requires", () => {
  const envelope = clone(sample("coverage", "refusal").envelope);
  envelope.refusal.code = "anchor_not_in_version";
  envelope.refusal.helpful_payload = { requested_anchor: "art_9", nearest_anchors: [], do_not_fall_back_to_full_text_search: true };
  assert.match(readV3Envelope(envelope, contract).reason, /nearest anchors/);
  envelope.refusal.helpful_payload.nearest_anchors = ["art_8"];
  assert.equal(readV3Envelope(envelope, contract).ok, true);
  envelope.refusal.helpful_payload.do_not_fall_back_to_full_text_search = false;
  assert.equal(readV3Envelope(envelope, contract).ok, false);
});
