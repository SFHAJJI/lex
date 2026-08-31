import assert from "node:assert/strict";
import test from "node:test";

import { readFile } from "node:fs/promises";

import { loadCaptured } from "../scripts/captured-envelopes.mjs";
import { decodeEnvelope, resolveLocalRef, validateEnvelope } from "../scripts/envelope.mjs";

// Exactly the pairing build.mjs uses. Validating a captured envelope against any other
// schema proves nothing about the page that actually ships.
const json = async (url) => JSON.parse(await readFile(url, "utf8"));
const schema = await json(
  new URL("../../schemas/v3-synthetic-preview/synthetic-resolve-envelope.schema.json", import.meta.url),
);
const registry = new Map([
  [
    "lex-v3-preview-object-set/1",
    await json(new URL("../../schemas/v3-preview/preview-object-set.schema.json", import.meta.url)),
  ],
]);

/** The build's own gate: decode, then validate what decoded. */
function inspect(envelope) {
  const { decoded, problems } = decodeEnvelope(schema, envelope, registry);
  return problems.concat(decoded ? validateEnvelope(schema, decoded) : []);
}

/**
 * Codex's four exact mutations against the captured refusal envelope.
 *
 * Every one of them returned `decodeProblems=[]` and `validateProblems=[]` before this
 * repair. The first three cross a real `$ref` boundary that the reader never followed;
 * the fourth violates a tuple position pinned by `const`, which the reader never looked
 * at because it only read `items`. A validator that skips what it does not implement
 * reports everything as clean, which is worse than having no validator at all.
 */
const MUTATIONS = [
  {
    name: "remove context.operation.operation_id",
    apply: (e) => {
      delete e.context.operation.operation_id;
    },
  },
  {
    name: "replace context.operation.catalog_sha256 with a non-hex value",
    apply: (e) => {
      e.context.operation.catalog_sha256 = "nothex";
    },
  },
  {
    name: "add context.operation.smuggled",
    apply: (e) => {
      e.context.operation.smuggled = true;
    },
  },
  {
    name: "replace publisher_contexts_checked with an undeclared publisher",
    apply: (e) => {
      e.context.publisher_contexts_checked = ["not-a-publisher"];
    },
  },
];

test("the captured refusal envelope is clean before any mutation", () => {
  assert.deepEqual(inspect(loadCaptured("refusal.json")), []);
});

for (const mutation of MUTATIONS) {
  test(`refused: ${mutation.name}`, () => {
    const envelope = loadCaptured("refusal.json");
    mutation.apply(envelope);

    const problems = inspect(envelope);

    assert.ok(
      problems.length > 0,
      `${mutation.name} was reported clean by both the validator and the decoder`,
    );
  });
}

test("a local $ref resolves to the node it points at", () => {
  const problems = [];
  const doc = { $defs: { thing: { type: "string" } } };
  assert.deepEqual(
    resolveLocalRef("#/$defs/thing", doc, "(root)", problems, new Set()),
    { type: "string" },
  );
  assert.deepEqual(problems, []);
});

test("an unresolvable, external or cyclic $ref refuses rather than passing", () => {
  const doc = { $defs: {} };
  for (const [ref, seen] of [
    ["#/$defs/missing", new Set()],
    ["https://example.invalid/schema#/x", new Set()],
    ["not-a-pointer", new Set()],
    ["#/$defs/loop", new Set(["#/$defs/loop"])],
  ]) {
    const problems = [];
    assert.equal(resolveLocalRef(ref, doc, "(root)", problems, seen), null);
    assert.equal(problems.length, 1, `${ref} produced ${problems.length} problems`);
  }
});

test("a JSON pointer escape is decoded", () => {
  const problems = [];
  const doc = { "a/b": { "c~d": { const: 1 } } };
  assert.deepEqual(
    resolveLocalRef("#/a~1b/c~0d", doc, "(root)", problems, new Set()),
    { const: 1 },
  );
  assert.deepEqual(problems, []);
});

test("an implemented composition keyword is applied rather than refused", () => {
  const composed = {
    anyOf: [
      {
        properties: {
          branch: { const: "success" },
          digest: { allOf: [{ type: "string" }, { minLength: 4 }] },
        },
      },
    ],
  };
  assert.deepEqual(validateEnvelope(composed, { branch: "success", digest: "abcd" }), []);
  assert.ok(
    validateEnvelope(composed, { branch: "success", digest: "ab" }).length > 0,
    "an allOf arm was skipped",
  );
});

test("a conditional subschema selects the branch it names", () => {
  const conditional = {
    anyOf: [
      {
        properties: { branch: { const: "success" }, kind: { type: "string" }, n: {} },
        if: { properties: { kind: { const: "hex" } } },
        then: { properties: { n: { pattern: "^[0-9a-f]+$", type: "string" } } },
        else: { properties: { n: { type: "boolean" } } },
      },
    ],
  };
  assert.deepEqual(validateEnvelope(conditional, { branch: "success", kind: "hex", n: "ab" }), []);
  assert.ok(
    validateEnvelope(conditional, { branch: "success", kind: "hex", n: "zz" }).length > 0,
    "the then branch was not applied",
  );
  assert.deepEqual(validateEnvelope(conditional, { branch: "success", kind: "other", n: true }), []);
  assert.ok(
    validateEnvelope(conditional, { branch: "success", kind: "other", n: "ab" }).length > 0,
    "the else branch was not applied",
  );
});

test("a schema keyword this reader does not implement refuses the position", () => {
  const problems = [];
  const armless = {
    anyOf: [
      {
        properties: {
          branch: { const: "success" },
          weird: { not: { type: "number" } },
        },
      },
    ],
  };
  const found = validateEnvelope(armless, { branch: "success", weird: "x" });
  assert.ok(
    found.some((problem) => problem.includes("not implemented")),
    `expected an unimplemented-keyword refusal, got ${JSON.stringify(found)}`,
  );
  assert.deepEqual(problems, []);
});

test("a closed tuple refuses an extra position and a wrong const", () => {
  const tupleSchema = {
    anyOf: [
      {
        properties: {
          branch: { const: "success" },
          pair: {
            type: "array",
            prefixItems: [{ const: "lu-legilux" }, { type: "string" }],
            items: false,
          },
        },
      },
    ],
  };

  assert.deepEqual(
    validateEnvelope(tupleSchema, { branch: "success", pair: ["lu-legilux", "ok"] }),
    [],
  );

  const wrongConst = validateEnvelope(tupleSchema, { branch: "success", pair: ["nope", "ok"] });
  assert.ok(wrongConst.length > 0, "a wrong tuple const passed");

  const tooLong = validateEnvelope(tupleSchema, {
    branch: "success",
    pair: ["lu-legilux", "ok", "extra"],
  });
  assert.ok(tooLong.length > 0, "an over-long closed tuple passed");

  const tooShort = validateEnvelope(tupleSchema, { branch: "success", pair: ["lu-legilux"] });
  assert.ok(tooShort.length > 0, "a short tuple passed");
});
