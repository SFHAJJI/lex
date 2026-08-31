// Induced mutations for the envelope validator.
//
// The validator is what stops an invented or drifted fixture reaching a rendered
// page, so it is the piece that most needs to be shown red. Each test breaks one
// property on purpose; the passing cases exist so the suite cannot be satisfied by a
// validator that rejects everything.

import assert from "node:assert/strict";
import test from "node:test";

import { loadEnvelopeSchema, validateEnvelope, STATES } from "../scripts/envelope.mjs";

const schema = await loadEnvelopeSchema();

/** The success arm of the published schema, so fixtures are derived rather than invented. */
function successArm() {
  return schema.anyOf.find((arm) => arm?.properties?.branch?.const === "success");
}

/** Build the smallest envelope the schema's own const values imply. */
function minimalSuccess() {
  const arm = successArm();
  const out = {};
  for (const key of arm.required) {
    const node = arm.properties[key];
    out[key] = materialise(node);
  }
  out.branch = "success";
  return out;
}

function materialise(node) {
  if (!node) return null;
  if (node.const !== undefined) return node.const;
  if (node.enum) return node.enum[0];
  if (node.type === "string") {
    // A `format` is part of the contract just as much as a pattern is. Deriving "x" for
    // a date-time field produced a fixture the published schema rejects, and the only
    // reason it ever passed is that the validator did not check formats at all.
    if (node.format === "date-time") return "2026-08-31T00:00:00Z";
    if (node.format === "uri") return "https://example.invalid/derived";
    const length = Math.max(1, node.minLength ?? 1);
    // Honour the published pattern rather than guessing at a filler. A digest field
    // demands hexadecimal, and a fixture that ignores that is not derived from the
    // contract at all - it only looks like it is.
    const candidates = ["0", "a", "x"].map((c) => c.repeat(length));
    if (!node.pattern) return candidates[2];
    const re = new RegExp(node.pattern, "u");
    const match = candidates.find((c) => re.test(c));
    if (!match) {
      throw new Error(`cannot derive a value satisfying ${node.pattern}`);
    }
    return match;
  }
  if (node.type === "boolean") return true;
  if (node.type === "array") return [];
  if (node.type === "object" || node.properties) {
    const out = {};
    for (const key of node.required ?? []) {
      out[key] = materialise(node.properties?.[key]);
    }
    return out;
  }
  return null;
}

test("the five renderable states are the closed set", () => {
  assert.deepEqual([...STATES], [
    "success",
    "refusal",
    "loading",
    "transport_failure",
    "invalid_envelope",
  ]);
});

test("an envelope derived from the schema's own constraints validates", () => {
  const problems = validateEnvelope(schema, minimalSuccess());
  assert.deepEqual(problems, [], problems.join("; "));
});

test("a non-object is rejected rather than coerced", () => {
  assert.equal(validateEnvelope(schema, "not an envelope").length, 1);
  assert.equal(validateEnvelope(schema, null).length, 1);
  assert.equal(validateEnvelope(schema, [1, 2]).length, 1);
});

test("an unknown branch is rejected, naming the branches that exist", () => {
  const problems = validateEnvelope(schema, { branch: "sideways" });
  assert.equal(problems.length, 1);
  assert.match(problems[0], /is not one of/);
});

test("a missing branch member is rejected", () => {
  const problems = validateEnvelope(schema, { status: "ok" });
  assert.match(problems[0], /no string branch/);
});

test("a missing required member is reported by name", () => {
  const envelope = minimalSuccess();
  delete envelope.context;
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.some((p) => p.includes("missing required member context")), problems.join("; "));
});

test("a wrong const value is rejected, not tolerated", () => {
  const envelope = minimalSuccess();
  envelope.schema = "lex-v3-preview-envelope/2";
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.some((p) => p.includes("expected const")), problems.join("; "));
});

test("a refusal status on the success arm is rejected", () => {
  const envelope = minimalSuccess();
  envelope.status = "refused";
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.length > 0, "status is a const on the success arm and must be enforced");
});

test("an unexpected member is rejected where the schema closes the object", () => {
  const envelope = minimalSuccess();
  envelope.result.smuggled = "extra";
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.some((p) => p.includes("unexpected member smuggled")), problems.join("; "));
});

test("a malformed sha256 is rejected by the published pattern", () => {
  const envelope = minimalSuccess();
  envelope.result.object_set_sha256 = "nothex";
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.some((p) => p.includes("pattern")), problems.join("; "));
});

test("every problem is reported, not only the first", () => {
  const envelope = minimalSuccess();
  envelope.schema = "wrong";
  envelope.object_type = "wrong";
  const problems = validateEnvelope(schema, envelope);
  assert.ok(problems.length >= 2, `expected several problems, got ${problems.length}`);
});
