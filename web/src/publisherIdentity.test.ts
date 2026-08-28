import assert from "node:assert/strict";
import test from "node:test";
import { MAX_PUBLISHER_IDENTITY, publisherIdentity } from "./publisherIdentity.ts";
import { envelopeStripRows } from "./envelopeStrip.ts";
import { normalizeSearchResponse } from "./searchPopulation.ts";
import { classifyEnvelope, LIMITATION_STATUS, validateLimitation } from "./limitations.ts";

/**
 * The publisher identity is the join key across three independent disclosures: which index a row
 * came from (the strip), which denominator applies to it (the population footer) and which
 * limitation explains a gap (the capability list). These tests hold the three to ONE grammar.
 *
 * Two failure modes, and they pull in opposite directions, so both are asserted:
 *   ALIASING  " lu-legilux " or "LU-Legilux" becoming lu-legilux, so one publisher's rows are
 *             checked against another logical identity's denominator.
 *   SPLITTING one raw value accepted by one module and refused by another, so a population is
 *             voided in one place and honoured in another.
 */

// ---------------------------------------------------------------------------
// The grammar itself
// ---------------------------------------------------------------------------

/** Everything the producer can mint, plus the widest values inside its declared class. */
const ACCEPTED = [
  "lu-legilux",
  "eu-eurlex",
  "a",
  "publisher_2",
  "x0-9_z",
  "x".repeat(MAX_PUBLISHER_IDENTITY),
];

/**
 * The hostile table. Shared by the grammar tests and by the cross-module property below, so a
 * future divergence between the three modules fails a test rather than passing silently.
 *
 * Padding and case aliases lead, because they are the two attacks that turn one raw value into a
 * different logical identity. The rest close the surrounding grammar.
 */
const REFUSED: unknown[] = [
  " lu-legilux ",
  " lu-legilux",
  "lu-legilux ",
  "\tlu-legilux",
  "lu-legilux\t",
  "lu-legilux\n",
  "\nlu-legilux",
  "lu-legilux\r\n",
  "lu-legilux\u00a0",
  "\u200blu-legilux",
  "LU-LEGILUX",
  "LU-Legilux",
  "Lu-Legilux",
  "lu-legiluX",
  "EU-EurLex",
  "?",
  "lu legilux",
  "lu:legilux",
  "lu.legilux",
  "lu/legilux",
  "lu+legilux",
  "",
  "x".repeat(MAX_PUBLISHER_IDENTITY + 1),
  " ".repeat(MAX_PUBLISHER_IDENTITY),
  7,
  0,
  null,
  undefined,
  true,
  {},
  [],
  ["lu-legilux"],
  { toString: () => "lu-legilux" },
];

test("the shared validator returns the raw value and never a repaired one", () => {
  for (const value of ACCEPTED) {
    const verdict = publisherIdentity(value);
    assert.equal(verdict, value, `refused a mintable identity ${JSON.stringify(value)}`);
    // Reference equality, not merely equal text: callers key Maps by this, so a validator that
    // returned a rebuilt string would still be non-normalizing only by accident.
    assert.ok(verdict === value, "the validator returned a different string object");
  }
});

test("a padded publisher is refused, never trimmed into a valid identity", () => {
  // Trimming is how one raw value becomes a DIFFERENT logical identity. " lu-legilux " is not
  // lu-legilux, and cleaning it here would hand the strip a key the footer never agreed to.
  for (const padded of [" lu-legilux ", " lu-legilux", "lu-legilux ", "\tlu-legilux",
                        "lu-legilux\n", "lu-legilux\u00a0"]) {
    assert.equal(publisherIdentity(padded), undefined,
      `accepted padded publisher ${JSON.stringify(padded)}`);
    assert.notEqual(publisherIdentity(padded), "lu-legilux",
      `repaired ${JSON.stringify(padded)} into a valid identity`);
  }
});

test("an uppercase or mixed-case alias is refused, never folded to the lower-case identity", () => {
  // The producer's registry is an ordinal dictionary of lower-case keys, so an uppercase spelling
  // is a value it cannot mint. The old regexes carried the `i` flag and admitted every one of
  // these as an identity of its own.
  for (const alias of ["LU-LEGILUX", "LU-Legilux", "Lu-Legilux", "lu-legiluX", "EU-EurLex"]) {
    assert.equal(publisherIdentity(alias), undefined,
      `accepted case alias ${JSON.stringify(alias)}`);
    assert.notEqual(publisherIdentity(alias), alias.toLowerCase(),
      `normalized ${JSON.stringify(alias)} into the lower-case identity`);
  }
});

test("the grammar is bounded, closed and total over every hostile value", () => {
  for (const value of REFUSED) {
    assert.equal(publisherIdentity(value), undefined,
      `accepted hostile publisher ${JSON.stringify(value)}`);
  }
  assert.equal(publisherIdentity("x".repeat(MAX_PUBLISHER_IDENTITY))?.length,
    MAX_PUBLISHER_IDENTITY, "the bound itself must remain acceptable");
  assert.equal(MAX_PUBLISHER_IDENTITY, 64,
    "the producer's own bound: UiEffects.Identifier and LegalOperationCatalog.MaximumShortLength");
});

// ---------------------------------------------------------------------------
// The three seams, held to one answer
// ---------------------------------------------------------------------------

/** A coherent `ok` population, so only the publisher is under test. */
const okPopulation = (works: number) => ({
  basis: "selected_metadata_scope",
  scope_filters_applied: true,
  query_ran: true,
  works_in_scope: works,
  known_exclusions: [],
});

const ranEntry = (publisher: unknown, works = 100) => ({
  envelope: { status: "ok", publisher },
  retrieval_mode: "keyword",
  hits: [{ lex_id: "w:0", title: "t" }],
  population: okPopulation(works),
});

/** The strip's seam: a row exists and is keyed by the raw value. */
const stripAccepts = (value: unknown): boolean => {
  const rows = envelopeStripRows([{ envelope: { publisher: value } }]);
  return rows.length === 1 && rows[0]!.publisher === value;
};

/** The population footer's seam: a denominator is attributed to the raw value. */
const populationAccepts = (value: unknown): boolean => {
  const normalized = normalizeSearchResponse([ranEntry(value)], classifyEnvelope);
  return normalized.populations.length === 1 && normalized.populations[0]!.publisher === value;
};

/** The limitation list's seam: the refusal carries the raw value as its publisher. */
const limitationAccepts = (value: unknown): boolean => {
  const limitation = validateLimitation({
    status: LIMITATION_STATUS,
    tool: "search",
    publisher: value,
    unsupported_filters: ["domain"],
  });
  // `publisher` is optional on a limitation, so an absent one is undefined and would compare
  // equal to an absent input. Acceptance means the raw value came back, not that nothing did.
  return limitation !== null && limitation.publisher !== undefined
    && limitation.publisher === value;
};

test("strip, population and limitation accept exactly the same publisher identities", () => {
  // The property, over the shared hostile table. A future divergence between the three fails
  // HERE rather than passing silently and surfacing as a denominator beside foreign rows.
  for (const value of [...ACCEPTED, ...REFUSED]) {
    const expected = publisherIdentity(value) !== undefined;
    const label = JSON.stringify(value) ?? String(value);
    assert.equal(stripAccepts(value), expected, `strip disagreed about ${label}`);
    assert.equal(populationAccepts(value), expected, `population disagreed about ${label}`);
    assert.equal(limitationAccepts(value), expected, `limitation disagreed about ${label}`);
  }
});

test("the three seams are live: a mintable identity really is accepted by all of them", () => {
  // An always-false trio would satisfy the property above forever. This is the non-trivial
  // baseline: the fixtures must actually reach each validator with something it accepts.
  assert.ok(stripAccepts("lu-legilux"), "the strip fixture never produced a row");
  assert.ok(populationAccepts("lu-legilux"), "the population fixture never produced a denominator");
  assert.ok(limitationAccepts("lu-legilux"), "the limitation fixture never produced a publisher");
  // And really refused by all of them, so the trio is not always-true either.
  assert.ok(!stripAccepts(" lu-legilux "));
  assert.ok(!populationAccepts(" lu-legilux "));
  assert.ok(!limitationAccepts(" lu-legilux "));
});

test("a padded and an unpadded spelling become neither two identities nor one", () => {
  // Both directions of the defect at the population seam. Trimming merges them and the padded
  // entry's denominator silently replaces or conflicts with the real one; accepting the padding
  // as its own key splits one publisher into two rows of the footer.
  const normalized = normalizeSearchResponse(
    [ranEntry("lu-legilux", 100), ranEntry(" lu-legilux ", 999)], classifyEnvelope);
  assert.deepEqual(normalized.populations.map((p) => p.publisher), ["lu-legilux"],
    "the padded spelling became a second identity, or displaced the real one");
  assert.equal(normalized.populations[0]!.population.works_in_scope, 100,
    "a padded entry's denominator reached the reader");
  assert.equal(normalized.complete, false,
    "an unattributable entry was silently dropped instead of disclosed");
  assert.equal(normalized.complete === false && normalized.unattributedEntries, 1);
  assert.deepEqual(normalized.complete === false && normalized.withheldPublishers, [],
    "an unnamed entry cannot void the publisher it was pretending to be");
});

test("a case alias becomes neither a second identity nor the lower-case one", () => {
  const normalized = normalizeSearchResponse(
    [ranEntry("lu-legilux", 100), ranEntry("LU-Legilux", 999)], classifyEnvelope);
  assert.deepEqual(normalized.populations.map((p) => p.publisher), ["lu-legilux"]);
  assert.equal(normalized.populations[0]!.population.works_in_scope, 100);
  assert.equal(normalized.complete === false && normalized.unattributedEntries, 1);
});
