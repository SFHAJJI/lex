// The V3 envelope, read the way the platform reads it.
//
// This mirrors `V3EnvelopeJson.ParseAndVerify` and the builder's binding checks in
// `src/Lex.V3.Contracts/Platform/`: every object has its exact closed member set, the fixed
// values are the fixed values, the registry digest is the reviewed one, exactly one of result
// and refusal is present and the verdict and context status agree with it, the result is bound
// to the operation's result schema and object types, and a refusal carries a registry code with
// every mandatory payload field.
//
// The contract (the registry digest, every operation's result schema and object types, every
// refusal code and its mandatory fields) is not written here. It is passed in, read from
// `schemas/v3-platform/envelope-samples.json`, which the platform renders from the reviewed
// registry, so this reader cannot hold a copy of the registry that drifts from it.
//
// Two checks the platform makes are not made here, and are named so nobody cites this for more:
// the platform refuses a duplicate member, and `JSON.parse` keeps the last one silently; and the
// platform requires the canonical bytes, which a browser cannot reproduce exactly (the platform's
// encoder escapes characters `JSON.stringify` does not). What this reads is the parsed value.

const ROOT_MEMBERS = [
  "context", "object_type", "operation_id", "refusal", "registry_schema", "registry_sha256",
  "request_ref", "result", "schema", "verdict", "version",
];
const CONTEXT_MEMBERS = [
  "freshness", "jurisdiction", "provisional", "publisher", "snapshot", "status", "timeline_semantics",
];
const SNAPSHOT_MEMBERS = ["snapshot_id", "snapshot_sha256"];
const FRESHNESS_MEMBERS = ["observed_at", "upstream_health"];
const RESULT_MEMBERS = ["object_type", "schema", "value"];
const REFUSAL_MEMBERS = ["code", "helpful_payload", "schema"];

/** Each publisher's timeline semantics and jurisdiction; the platform refuses any other pairing. */
const PUBLISHERS = Object.freeze({
  "lu-legilux": Object.freeze({ timeline: "publisher_applicability", jurisdiction: "lu" }),
  "eu-eurlex": Object.freeze({ timeline: "official_consolidation_state", jurisdiction: "eu" }),
});

const UPSTREAM_HEALTH = Object.freeze(["current", "stale", "unreachable"]);
const SHA256 = /^[0-9a-f]{64}$/;
// The platform writes `DateTimeOffset.ToString("O")` of a UTC instant: seven fractional digits and Z.
const OBSERVED_AT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/;

const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

function exactMembers(value, expected, where) {
  if (!isObject(value)) return `${where} is not an object`;
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (actual.length !== wanted.length || actual.some((key, index) => key !== wanted[index])) {
    return `${where} does not have the exact closed member set: ${actual.join(",")}`;
  }
  return null;
}

function nonEmptyString(value) {
  return typeof value === "string" && value.length > 0;
}

/**
 * Reads one parsed envelope against the contract.
 *
 * @param {unknown} value the parsed response body
 * @param {object} contract the `contract` block of the envelope census
 * @param {{operation?: string, objectType?: string}} expected what the request asked for: the
 *   answer must echo the operation, and may be held to one object type
 * @returns {{ok: true, state: "success"|"refusal", envelope: object} | {ok: false, reason: string}}
 */
export function readV3Envelope(value, contract, expected = {}) {
  const fail = (reason) => ({ ok: false, reason });
  if (!isObject(contract) || !Array.isArray(contract.operations) || !Array.isArray(contract.refusals)) {
    return fail("no contract to read the envelope against");
  }

  let problem = exactMembers(value, ROOT_MEMBERS, "the envelope");
  if (problem) return fail(problem);
  if (value.schema !== contract.envelope_schema) return fail("unexpected schema");
  if (value.version !== contract.version) return fail("unexpected version");
  if (value.object_type !== "envelope") return fail("unexpected object_type");
  if (value.registry_schema !== contract.registry_schema) return fail("unexpected registry_schema");
  if (value.registry_sha256 !== contract.registry_sha256) {
    return fail("the envelope is not bound to the reviewed registry");
  }
  if (!nonEmptyString(value.request_ref)) return fail("request_ref is not a string");
  if (!nonEmptyString(value.operation_id)) return fail("operation_id is not a string");
  if (expected.operation !== undefined && value.operation_id !== expected.operation) {
    return fail(`the envelope answers ${value.operation_id}, not ${expected.operation}`);
  }
  const operation = contract.operations.find((entry) => entry.operation_id === value.operation_id);
  if (!operation) return fail("the operation is not a registered one");

  const context = value.context;
  problem = exactMembers(context, CONTEXT_MEMBERS, "the context");
  if (problem) return fail(problem);
  const publisher = PUBLISHERS[context.publisher];
  if (!publisher) return fail("unknown publisher");
  if (context.timeline_semantics !== publisher.timeline) {
    return fail("the timeline semantics do not match the publisher");
  }
  if (context.jurisdiction !== publisher.jurisdiction) return fail("the jurisdiction does not match the publisher");
  if (typeof context.provisional !== "boolean") return fail("provisional is not a boolean");
  problem = exactMembers(context.snapshot, SNAPSHOT_MEMBERS, "the snapshot");
  if (problem) return fail(problem);
  if (!nonEmptyString(context.snapshot.snapshot_id)) return fail("snapshot_id is not a string");
  if (typeof context.snapshot.snapshot_sha256 !== "string" || !SHA256.test(context.snapshot.snapshot_sha256)) {
    return fail("snapshot_sha256 is not a digest");
  }
  problem = exactMembers(context.freshness, FRESHNESS_MEMBERS, "the freshness");
  if (problem) return fail(problem);
  if (typeof context.freshness.observed_at !== "string" || !OBSERVED_AT.test(context.freshness.observed_at)) {
    return fail("observed_at is not the platform's UTC timestamp");
  }
  if (!UPSTREAM_HEALTH.includes(context.freshness.upstream_health)) return fail("unknown upstream health");

  if (!Array.isArray(contract.verdicts) || !contract.verdicts.includes(value.verdict)) return fail("unknown verdict");
  const hasResult = value.result !== null;
  const hasRefusal = value.refusal !== null;
  if (hasResult === hasRefusal) return fail("an envelope carries exactly one of result and refusal");

  if (hasRefusal) {
    if (value.verdict !== "refuse") return fail("a refusal carries the refuse verdict");
    if (context.status !== "refusal") return fail("a refusal's context status is refusal");
    const refusal = value.refusal;
    problem = exactMembers(refusal, REFUSAL_MEMBERS, "the refusal");
    if (problem) return fail(problem);
    if (refusal.schema !== contract.refusal_schema) return fail("unexpected refusal schema");
    const declared = contract.refusals.find((entry) => entry.code === refusal.code);
    if (!declared) return fail("the refusal code is not in the closed registry");
    if (!isObject(refusal.helpful_payload) || Object.keys(refusal.helpful_payload).length === 0) {
      return fail("a refusal carries a helpful payload");
    }
    const missing = declared.mandatory_payload_fields.filter((field) => !(field in refusal.helpful_payload));
    if (missing.length > 0) return fail(`the refusal payload is missing ${missing.join(", ")}`);
    if (refusal.code === "anchor_not_in_version") {
      const { nearest_anchors: anchors, do_not_fall_back_to_full_text_search: noFallback } = refusal.helpful_payload;
      if (!Array.isArray(anchors) || anchors.length === 0 || noFallback !== true) {
        return fail("anchor_not_in_version names its nearest anchors and forbids the full-text fallback");
      }
    }
    return { ok: true, state: "refusal", envelope: value };
  }

  if (value.verdict === "refuse") return fail("a result never carries the refuse verdict");
  if (context.status !== "success") return fail("a result's context status is success");
  const result = value.result;
  problem = exactMembers(result, RESULT_MEMBERS, "the result");
  if (problem) return fail(problem);
  if (result.schema !== operation.result_schema) return fail("the result schema is not bound to this operation");
  if (!operation.result_object_types.includes(result.object_type)) {
    return fail("the result object type is not bound to this operation");
  }
  if (expected.objectType !== undefined && result.object_type !== expected.objectType) {
    return fail(`the result is a ${result.object_type}, not a ${expected.objectType}`);
  }
  if (!isObject(result.value)) return fail("the result value is not an object");
  return { ok: true, state: "success", envelope: value };
}
