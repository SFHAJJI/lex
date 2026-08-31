// Fixture derivation tests.
//
// The point of these is that a fixture must be DERIVED from the real preview graph,
// never typed by hand. Every identity a fixture claims has to come from the control
// artifact the API itself serves from, so a fixture cannot assert a snapshot, catalog
// or builder identity the graph does not have.

import assert from "node:assert/strict";
import test from "node:test";

import {
  loadControl,
  successFixtures,
  registryCodes,
  requireCapturedFixture,
  UNAVAILABLE_DIGESTS,
} from "../scripts/fixtures.mjs";
import { loadEnvelopeSchema, validateEnvelope } from "../scripts/envelope.mjs";

const control = await loadControl();

test("exactly one control artifact exists in the preview graph", async () => {
  assert.ok(control.snapshot?.snapshot_sha256, "the control must carry a snapshot identity");
  assert.ok(control.builder?.source_sha256, "the control must carry a builder identity");
});

test("fixture identities are read from the graph, not written by hand", async () => {
  const [first] = await successFixtures(control);
  assert.equal(first.context.snapshot.snapshot_sha256, control.snapshot.snapshot_sha256);
  assert.equal(first.context.builder.source_sha256, control.builder.source_sha256);
  assert.equal(first.context.operation.catalog_id, control.operation_catalog.catalog_id);
  assert.equal(first.context.refusal_registry.registry_id, control.refusal_registry.registry_id);
});

test("two success fixtures differ in content, so derivation can be proven", async () => {
  const [a, b] = await successFixtures(control);
  assert.notEqual(a.result.object_set_id, b.result.object_set_id);
  assert.notEqual(a.result.object_set_sha256, b.result.object_set_sha256);
});

test("refusal codes come from the real registry", async () => {
  const codes = await registryCodes(control);
  assert.ok(codes.includes("identifier_unknown"), `registry declared ${JSON.stringify(codes)}`);
});

test("the derived fixture is still incomplete, and the validator says exactly why", async () => {
  // This is the honest state of the package: everything derivable is derived, and the
  // two runtime-computed digests are missing. The validator naming them is what stops
  // an invented value being substituted quietly.
  const schema = await loadEnvelopeSchema();
  const [first] = await successFixtures(control);
  const problems = validateEnvelope(schema, first);
  assert.ok(problems.length > 0, "an incomplete fixture must not validate");
  for (const member of UNAVAILABLE_DIGESTS) {
    const leaf = member.split(".").pop();
    assert.ok(
      problems.some((p) => p.includes(leaf)),
      `the validator must name ${member}; it reported ${problems.join("; ")}`,
    );
  }
});

test("the generator refuses to guess a digest it cannot compute", () => {
  assert.throws(requireCapturedFixture, (error) => {
    assert.match(error.message, /catalog_sha256/);
    assert.match(error.message, /plausible wrong value/);
    return true;
  });
});
