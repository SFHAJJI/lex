// One registry, read from the one place it is declared.
//
// S4-A01 says REST and MCP derive from one explicit operation registry. The reader is the third
// consumer of that registry and had its own copy: the platform closed its refusal vocabulary at
// twenty codes on 2026-09-17 (`95fec261`, the Stage 4 registry) and the card list has been closed
// at nineteen since 2026-09-01 (`937c13f3`), so `pinned_digest_mismatch` — a refusal the platform
// can produce — reached a reader that refuses to render a code it cannot name
// (`validateRefusal`, "the registry is closed and a code the client cannot name must not be
// rendered as a generic error"). That guard is right; the list behind it was stale.
//
// Adding the missing card fixes today. This file is what stops it happening to the twenty-first:
// the card list and its per-code payload contract are asserted against the C# registry itself,
// so the two cannot drift again without a red test naming the code that drifted.
//
// WHY IT READS THE SOURCE FILE. There is no rendered artifact listing the twenty: the schemas
// under `schemas/v3-platform` describe an envelope whose `helpful_payload` is an open object, and
// the registry's digest covers bytes the reader cannot decompose. So this parses the declaration.
// A parse is a liability — it can silently match nothing and pass — so every extraction here is
// checked for plausibility before it is compared, and the test fails loudly if the C# ever moves
// rather than quietly agreeing with an empty set.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";

const REGISTRY = new URL("../../src/Lex.V3.Contracts/Platform/V3OperationRegistry.cs", import.meta.url);

/** The `RequiredRefusalCodes` array, as the registry declares it. */
function codesIn(source) {
  const block = /private static readonly string\[\] RequiredRefusalCodes\s*=\s*\[([\s\S]*?)\];/.exec(source);
  assert.ok(block, "RequiredRefusalCodes is not where this test expects it in V3OperationRegistry.cs");
  const codes = [...block[1].matchAll(/"([a-z_]+)"/g)].map((match) => match[1]);
  // A parse that found the block and no codes would agree with anything.
  assert.ok(codes.length >= 15, `parsed ${codes.length} refusal codes, which is too few to be the registry`);
  return codes;
}

/** The `MandatoryRefusalPayloadFields` table, as the registry declares it. */
function mandatoryPayloadIn(source) {
  const block = /MandatoryRefusalPayloadFields\s*=[\s\S]*?\{([\s\S]*?)\n        \};/.exec(source);
  assert.ok(block, "MandatoryRefusalPayloadFields is not where this test expects it");
  const fields = new Map();
  for (const entry of block[1].matchAll(/\["([a-z_]+)"\]\s*=\s*\[([^\]]*)\]/g)) {
    fields.set(entry[1], [...entry[2].matchAll(/"([a-z_]+)"/g)].map((match) => match[1]));
  }

  assert.ok(fields.size >= 15, `parsed ${fields.size} payload entries, which is too few to be the registry`);
  return fields;
}

test("the reader's refusal cards are the platform's refusal codes, exactly", async () => {
  const { REFUSAL_CODES } = await import("../scripts/refusal-card.mjs");
  const source = await readFile(REGISTRY, "utf8");
  const platform = codesIn(source);

  // Sets, not order: the C# declares them alphabetically and the card list is grouped by meaning.
  // What must agree is which codes exist, and the message names the ones that do not.
  const cards = new Set(REFUSAL_CODES);
  const declared = new Set(platform);
  assert.deepEqual(
    [...declared].filter((code) => !cards.has(code)).sort(),
    [],
    "the platform can produce these and the reader has no card for them",
  );
  assert.deepEqual(
    [...cards].filter((code) => !declared.has(code)).sort(),
    [],
    "the reader has cards for these and the platform cannot produce them",
  );
  // And no duplicates hiding a missing one behind an equal count.
  assert.equal(cards.size, REFUSAL_CODES.length);
  assert.equal(declared.size, platform.length);
});

test("every code the platform declares has a contract on the reader's side or none at all", async () => {
  // What this can honestly assert today, and what it cannot.
  //
  // CAN: the two code lists agree (above), and every code the reader states a payload contract for
  // is a code the registry declares — a contract for a code that does not exist is dead weight
  // that will be mistaken for a rule.
  //
  // CANNOT: whether each contract matches the payload its producer sends. I tried to assert that
  // against the registry's `MandatoryRefusalPayloadFields` and the assertion was wrong in both
  // directions, which the card itself proves:
  //
  //   - the card REQUIRES every declared key, so a contract larger than the payload rejects a
  //     valid refusal for something missing;
  //   - the card REJECTS every undeclared key, so a contract smaller than the payload rejects a
  //     valid refusal for something extra.
  //
  // The registry's mandate is a MINIMUM. Producers send more: `diff` adds `left`, `right` and
  // `language` to `profiles_differ`, and `RefuseAmbiguousVersion` adds `bound` only when there is
  // one. So the reader's contract must equal the payload as SENT, and neither the registry nor any
  // schema says what that is — the `helpful_payload` in `refusal.schema.json` is an open object.
  //
  // Only a real payload from a real handler settles it, which is exactly what the writer seat's
  // `schemas/v3-platform/refusal-payload-samples.json` will carry. When that file lands, this test
  // loads it and renders every sample; until then it does not pretend to check what it cannot see.
  const { REQUIRED_PAYLOAD } = await import("../scripts/refusal-card.mjs");
  const declared = new Set(codesIn(await readFile(REGISTRY, "utf8")));

  for (const code of Object.keys(REQUIRED_PAYLOAD)) {
    assert.ok(declared.has(code), `the reader states a payload contract for ${code}, which the registry does not declare`);
  }

  // And the contract's own shape, so `optional` cannot quietly become a second required list.
  for (const [code, contract] of Object.entries(REQUIRED_PAYLOAD)) {
    // An empty `keys` is a rule, not a gap: the allowlist is then empty too, so the card refuses a
    // payload carrying anything at all. `advice_boundary` uses it deliberately — its obligation is
    // the governing text and a named counter, enforced in the renderer rather than as fields.
    assert.ok(Array.isArray(contract.keys), `${code}'s required keys are not a list`);
    assert.ok(typeof contract.basis === "string" && contract.basis.length > 0, `${code} cites no basis`);
    const overlap = (contract.optional ?? []).filter((key) => contract.keys.includes(key));
    assert.deepEqual(overlap, [], `${code} lists ${overlap.join(", ")} as both required and optional`);
  }
});
