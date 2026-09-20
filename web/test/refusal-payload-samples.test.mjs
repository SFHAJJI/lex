// The reader, against the payloads the platform actually sends.
//
// Two declarations agreeing prove nothing about a third thing: what a producer writes. Every
// defect this pair of files has had lived in that gap. The card required `publisher` and `work`
// for `ambiguous_version` and the mount sends neither; it required a `profiles` member that no
// producer emits; it wanted a `fallback_mode` from an operation that deliberately refuses rather
// than falling back; and it had no card at all for a code the mount produces. Each was found by
// looking at a payload, and none of them could have been found by comparing the reader's contract
// with the registry's, because the registry's mandate is a MINIMUM that producers exceed.
//
// So this test reads real payloads — one per produced code, taken from the real handler on the
// fixture mount by the writer seat's census — and puts each through the card. A refusal the
// platform can send and the reader cannot render is a failing test here, naming the code.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";

const SAMPLES = new URL("../../schemas/v3-platform/refusal-payload-samples.json", import.meta.url);

/**
 * Codes whose sample the card refuses today, each for a reason that is a PRODUCER gap and not a
 * reader defect — and each therefore a named, asserted break rather than a comment nobody reads.
 *
 * All three are one gap: the platform does not carry the evidence that an absence of record is not
 * an absence of law, which is this product's oldest invariant.
 *
 *   identifier_unknown      the card requires `population_disclosure`, the size of what was
 *                           searched. Without it "I do not know that identifier" reads as "no such
 *                           law exists". Deleting the requirement would delete the guarantee.
 *   no_version_for_date     the card requires `what_would_answer` and `asserts_absence_of_law`
 *   anchor_not_in_version   of every absence code; the mount sends neither for these two.
 *
 * This list is a RATCHET, checked in both directions: a code that starts rendering fails here
 * until it is removed, and a code that stops rendering fails here because it is not on the list.
 * A break the suite tolerates silently is a break the suite has stopped reporting.
 */
const KNOWN_BREAKS = ["anchor_not_in_version", "identifier_unknown", "no_version_for_date"];

async function samples() {
  let parsed;
  try {
    parsed = JSON.parse(await readFile(SAMPLES, "utf8"));
  } catch (error) {
    // Absent, unreadable or not JSON: this test measures nothing, and says so rather than passing.
    assert.fail(
      `the refusal payload samples could not be read (${error.code ?? error.name}): this test ` +
        "proves nothing without them, and a green run here would mean the reader had been checked",
    );
  }

  assert.ok(Array.isArray(parsed.produced), "the samples file declares no produced list");
  assert.ok(Array.isArray(parsed.not_produced), "the samples file declares no not_produced list");
  assert.ok(
    parsed.produced.length >= 5,
    `the samples file carries ${parsed.produced.length} produced payloads, too few to be the census`,
  );
  return parsed;
}

test("produced and not produced partition the reader's registry exactly", async () => {
  const { REFUSAL_CODES } = await import("../scripts/refusal-card.mjs");
  const file = await samples();
  const produced = file.produced.map((row) => row.code);
  const both = produced.filter((code) => file.not_produced.includes(code));

  assert.deepEqual(both, [], "a code is listed as both produced and not produced");
  assert.deepEqual(
    [...produced, ...file.not_produced].sort(),
    [...REFUSAL_CODES].sort(),
    "the census and the reader's registry are not the same twenty codes",
  );
});

test("every payload the platform sends is one the reader can render", async (t) => {
  const { renderRefusalCard } = await import("../scripts/refusal-card.mjs");
  const file = await samples();
  const broke = [];

  for (const row of file.produced) {
    assert.ok(row.payload && typeof row.payload === "object", `${row.code} carries no sample payload`);
    assert.ok(Array.isArray(row.produced_by) && row.produced_by.length > 0, `${row.code} names no producer`);

    try {
      const html = renderRefusalCard({
        code: row.code,
        sentence: "A sentence the producer supplies.",
        payload: row.payload,
      });
      assert.ok(html.includes("refusal"), `${row.code} rendered nothing recognisable`);
    } catch (error) {
      broke.push(`${row.code} (produced by ${row.produced_by.join(", ")}): ${error.message}`);
      continue;
    }

    // And once more without each optional key, because "optional" is a claim about the producer
    // that the card must actually honour: a key declared optional and then required is the same
    // defect as one required and never sent.
    for (const key of row.optional_payload_keys ?? []) {
      const without = { ...row.payload };
      delete without[key];
      renderRefusalCard({
        code: row.code,
        sentence: "A sentence the producer supplies.",
        payload: without,
      });
    }
  }

  const brokeCodes = broke.map((line) => line.split(" ")[0]).sort();
  for (const line of broke) t.diagnostic(line);

  // Both directions. A break that is fixed must leave the list, and a break that is new must not
  // hide behind it.
  assert.deepEqual(
    brokeCodes,
    [...KNOWN_BREAKS].sort(),
    "the refusals the reader cannot render are not the ones this test knows about",
  );
});
