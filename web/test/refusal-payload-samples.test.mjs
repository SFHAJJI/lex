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

/**
 * The coarse shape rules for one string value: a path stays a path, a hash-pinned URL keeps its
 * `--`. Defined once because they are applied to a scalar and to every element of a list, and a
 * second copy is how the two would come to disagree. A non-string on either side is not this
 * function's business and passes.
 */
function assertStringShape(where, shown, real) {
  if (typeof real !== "string" || typeof shown !== "string") return;
  assert.equal(
    shown.startsWith("/"),
    real.startsWith("/"),
    `${where}: the producer sends ${real.startsWith("/") ? "a path" : "not a path"} and the example shows ${JSON.stringify(shown.slice(0, 40))}`,
  );
  assert.equal(
    shown.includes("--"),
    real.includes("--"),
    `${where}: the producer ${real.includes("--") ? "pins a digest after --" : "pins no digest"} and the example shows ${JSON.stringify(shown.slice(0, 40))}`,
  );
}

test("a worked example carries the fields its real payload carries", async (t) => {
  // The catalog is the page that teaches a reader what each refusal looks like, and its examples
  // were written by hand from the specification. One of them taught a URL grammar no producer
  // speaks -- `publisher:work` for a stable coordinate and a `/w/...` path for a hash-pinned URL,
  // neither of which this surface's own parser accepts. A reader building against that page would
  // have built against a language that does not exist.
  //
  // So an example for a code the platform produces must carry the same fields the producer sends.
  // The VALUES stay synthetic on purpose: the page's banner promises that nothing on it is a real
  // coordinate, and this check must not be a reason to put real ones there.
  const { REFUSAL_EXAMPLES } = await import("../scripts/refusal-catalog.mjs");
  const { ABSENCE_CODES } = await import("../scripts/refusal-card.mjs");
  const file = await samples();

  for (const row of file.produced) {
    // The three the card cannot render are exempt, and not by oversight: their examples show what
    // the CARD requires, which is deliberately more than the producer sends -- the population
    // disclosure, and the evidence that an absence of record is not an absence of law. Holding
    // their examples to the producer's payload would mean deleting from the page exactly the
    // fields this surface is refusing to give up. They rejoin this check the day the producer
    // carries them, because KNOWN_BREAKS shrinks and this list is the same one.
    if (KNOWN_BREAKS.includes(row.code)) {
      t.diagnostic(`${row.code}: example exempt while the producer sends no absence evidence`);
      continue;
    }

    const example = REFUSAL_EXAMPLES[row.code];
    if (!example) {
      t.diagnostic(`${row.code}: the platform produces it and the catalog has no worked example`);
      continue;
    }

    const required = Object.keys(row.payload).filter(
      (key) => !(row.optional_payload_keys ?? []).includes(key),
    );
    const missing = required.filter((key) => !Object.hasOwn(example.payload ?? {}, key));
    assert.deepEqual(
      missing,
      [],
      `${row.code}: the example omits ${missing.join(", ")}, which every producer of this refusal sends`,
    );

    // FIELD NAMES ARE NOT ENOUGH, and I only learned that by breaking this test on purpose: with
    // the names checked and nothing else, putting back the `publisher:work` colon form and the
    // `/w/...` path — the exact defect this whole check was written for — left it green. A name is
    // right and a value can still be in a grammar no producer speaks.
    //
    // So the VALUES are compared by shape, coarsely and deliberately: a real value that is a list
    // must be a list here; one that begins with `/` must begin with `/`; one that carries the `--`
    // of a hash-pinned URL must carry it. Not a grammar validator — the examples are synthetic and
    // must stay synthetic — but enough that a form nothing emits cannot sit on the page that
    // teaches the form.
    for (const [key, real] of Object.entries(row.payload)) {
      const shown = example.payload?.[key];
      if (shown === undefined) continue;
      assert.equal(
        Array.isArray(shown),
        Array.isArray(real),
        `${row.code}.${key}: the example is ${Array.isArray(shown) ? "a list" : "not a list"} and the producer sends ${Array.isArray(real) ? "one" : "none"}`,
      );
      // Inside a list too. Both being lists is not enough: the producer sends `ambiguous_version`'s
      // candidates as hash-pinned URL STRINGS and the example taught them as objects, and a check
      // that stopped at `Array.isArray` called that agreement.
      if (Array.isArray(real) && Array.isArray(shown)) {
        // An empty example list declares nothing about its rows and passed everything below, which
        // the writer seat proved with an `available_languages: []` example that no check touched.
        // It is the same hole I praised THEM for closing on the coverage pin, left open here: an
        // empty array hides its row shape, so a guard that skips it is green about nothing.
        if (real.length > 0) {
          assert.ok(
            shown.length > 0,
            `${row.code}.${key}: the producer sends a non-empty list and the example shows an empty one, which teaches a reader nothing about the rows`,
          );
        }

        // EVERY element, not just the first. Checking `shown[0]` alone let a list that starts with
        // the right shape and continues with the wrong one through, and the reference shape has to
        // come from a producer list that agrees with itself -- so if it ever does not, this fails
        // loudly rather than silently picking one of the shapes it sends.
        if (real.length > 0) {
          const kinds = new Set(real.map((element) => typeof element));
          assert.equal(
            kinds.size,
            1,
            `${row.code}.${key}: the producer sends a list of mixed kinds (${[...kinds].join(", ")}); this check cannot choose one of them for you`,
          );
          shown.forEach((element, index) => {
            assert.equal(
              typeof element,
              typeof real[0],
              `${row.code}.${key}[${index}]: the producer sends a list of ${typeof real[0]}s and this element's kind is ${typeof element}`,
            );
            assertStringShape(`${row.code}.${key}[${index}]`, element, real[0]);
          });
        }
      }

      assertStringShape(`${row.code}.${key}`, shown, real);
    }

    // The absence pair is allowed only where the card demands it. Exempting the two names for
    // EVERY code let any example carry them unnoticed, which a surviving mutant proved: the
    // exemption is for absence codes, and writing it as a blanket one made it a hole.
    const absenceExtras = ABSENCE_CODES.includes(row.code)
      ? ["what_would_answer", "asserts_absence_of_law"]
      : [];
    const invented = Object.keys(example.payload ?? {}).filter(
      (key) => !Object.hasOwn(row.payload, key) && !absenceExtras.includes(key),
    );
    assert.deepEqual(
      invented,
      [],
      `${row.code}: the example carries ${invented.join(", ")}, which no producer sends — the page would teach a field that does not exist`,
    );
  }
});
