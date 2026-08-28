import assert from "node:assert/strict";
import test from "node:test";
import { envelopeStripRows, indexFreshnessLabel } from "./envelopeStrip.ts";

const envelope = (publisher: string, over: Record<string, unknown> = {}) => ({
  envelope: {
    publisher,
    timeline_semantics: "publisher_applicability",
    freshness: {
      built_at: "2026-08-15T09:01:06Z",
      corpus_commit: "e9c4df09",
      stamp_signature_valid: true,
    },
    artifact: { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" },
    ...over,
  },
});

test("the strip carries the identity fields the envelope already published", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux")]);
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, "2026-08-15T09:01:06Z");
  assert.equal(row.signatureValid, true);
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.codeCommit, "abc123");
  assert.equal(row.manifestSetId, "m-1");
  assert.equal(row.contentDigest, "d-1");
});

test("freshness stays per publisher and is never collapsed into one build date", () => {
  // Two indexes built a week apart have two answers. Picking one asserts a freshness the other
  // does not have, which is the whole failure mode rule 8 names.
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope("eu-eurlex", { freshness: { built_at: "2026-08-08T00:00:00Z" } }),
  ]);
  assert.equal(rows.length, 2);
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.notEqual(rows[0].builtAt, rows[1].builtAt);
});

test("a publisher repeated across entries yields one strip row", () => {
  assert.equal(envelopeStripRows([envelope("lu-legilux"), envelope("lu-legilux")]).length, 1);
});

test("a missing build date is stated, never omitted into an undated current", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux", { freshness: {} })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(indexFreshnessLabel(row.builtAt), "index build date unavailable");
  assert.equal(indexFreshnessLabel("2026-08-15T09:01:06Z"), "index built 2026-08-15T09:01:06Z");
});

test("a value of the wrong type becomes absent rather than rendered", () => {
  const [row] = envelopeStripRows([envelope("lu-legilux", {
    freshness: { built_at: 20260815, stamp_signature_valid: "yes", corpus_commit: "" },
    artifact: { code_commit: null, manifest_set_id: "x".repeat(129) },
  })]);
  assert.equal(row.builtAt, undefined);
  assert.equal(row.signatureValid, undefined, "a non-boolean must not become a signature claim");
  assert.equal(row.corpusCommit, undefined);
  assert.equal(row.codeCommit, undefined);
  assert.equal(row.manifestSetId, undefined, "an unbounded identity must not render");
});

// The producer mints built_at as now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") in
// IndexFromCorpus.cs, and McpCore.cs copies that stamp entry into the envelope unchanged. Both
// values the live coverage tool returned on 2026-08-28 have exactly this shape.
const PRODUCER_BUILT_AT = "2026-08-15T09:22:08Z";

const builtAtOf = (value: unknown) =>
  envelopeStripRows([envelope("lu-legilux", { freshness: { built_at: value } })])[0].builtAt;

test("a canonical producer build date round-trips into the strip unchanged", () => {
  assert.equal(builtAtOf(PRODUCER_BUILT_AT), PRODUCER_BUILT_AT);
  assert.equal(
    indexFreshnessLabel(builtAtOf(PRODUCER_BUILT_AT)),
    `index built ${PRODUCER_BUILT_AT}`,
  );
  // Boundary instants the producer can legitimately mint must survive too, or the validation
  // would be refusing real builds and calling a dated index undated.
  for (const stamp of ["2024-02-29T00:00:00Z", "2026-12-31T23:59:59Z", "2026-01-01T00:00:00Z"]) {
    assert.equal(builtAtOf(stamp), stamp, `refused a build date the producer can emit: ${stamp}`);
  }
  // A two-digit year is inside the frozen grammar. Date.UTC maps 0-99 onto 1900-1999, so the
  // calendar check has to be made against the year the producer actually wrote. Returned
  // verbatim, never silently reinterpreted as 1926 and never refused because of that mapping.
  assert.equal(builtAtOf("0026-02-28T09:22:08Z"), "0026-02-28T09:22:08Z");
});

test("a bounded string that is not the producer's grammar never becomes a build date", () => {
  // The failure this closes: any bounded string was taken as a build date, so a synthetic
  // envelope could print "index built tomorrow" while the module claimed to fail closed.
  for (const value of [
    "tomorrow",
    "now",
    "2026-08-15",
    "15/08/2026",
    "2026-08-15T09:22:08",
    "2026-08-15T09:22:08.123Z",
    "2026-08-15T09:22:08+02:00",
    "2026-08-15t09:22:08z",
    "2026-08-15 09:22:08Z",
    " 2026-08-15T09:22:08Z",
    "2026-08-15T09:22:08Z ",
    "2026-08-15T09:22:08Z\n",
  ]) {
    assert.equal(builtAtOf(value), undefined, `rendered a build date from ${JSON.stringify(value)}`);
    assert.equal(indexFreshnessLabel(builtAtOf(value)), "index build date unavailable");
  }
});

test("a date that is well formed but does not exist is refused, not rolled over", () => {
  // new Date("2026-02-31") answers 3 March rather than failing, so the grammar alone would print
  // a day that never happened. Refusing means the round trip catches the rollover.
  for (const value of [
    "2026-02-31T09:22:08Z",
    "2025-02-29T09:22:08Z",
    "0026-02-31T09:22:08Z",
    "2026-13-01T09:22:08Z",
    "2026-00-10T09:22:08Z",
    "2026-04-31T09:22:08Z",
    "2026-08-00T09:22:08Z",
    "2026-08-32T09:22:08Z",
    "2026-08-15T24:00:00Z",
    "2026-08-15T09:60:08Z",
    "2026-08-15T09:22:60Z",
  ]) {
    assert.equal(builtAtOf(value), undefined, `printed an impossible instant: ${value}`);
  }
  // And it must not normalize one into a different identity either.
  assert.notEqual(builtAtOf("2026-02-31T09:22:08Z"), "2026-03-03T09:22:08Z");
});

test("a year the producer's own type cannot mint is refused", () => {
  // Not an arbitrary narrowing of the observed format: built_at is minted from a .NET DateTime,
  // and DateTime.MinValue is 0001-01-01, so 0000 is unmintable at the mint site. It is also the
  // only sub-0001 year four digits can express, so this closes the whole range below the
  // producer's. Reachable at all only because the year correction stops Date.UTC folding it
  // into 1900, which is why the two must sit side by side.
  assert.equal(builtAtOf("0000-01-01T00:00:00Z"), undefined);
  assert.equal(indexFreshnessLabel(builtAtOf("0000-01-01T00:00:00Z")), "index build date unavailable");
  assert.equal(builtAtOf("0000-12-31T23:59:59Z"), undefined);
  // The first instant the producer's type can actually mint is on the accepted side of the bound.
  assert.equal(builtAtOf("0001-01-01T00:00:00Z"), "0001-01-01T00:00:00Z");
  // DateTime.MaxValue is 9999-12-31, which four anchored digits already cap, so the top of the
  // range needs no guard of its own and a five-digit year cannot reach the check at all.
  assert.equal(builtAtOf("9999-12-31T23:59:59Z"), "9999-12-31T23:59:59Z");
  assert.equal(builtAtOf("10000-01-01T00:00:00Z"), undefined);
});

test("an overlong build date is refused, padded around a real stamp or not", () => {
  assert.equal(builtAtOf("2".repeat(300)), undefined);
  assert.equal(builtAtOf("x".repeat(129)), undefined);
  // A real stamp buried in junk must not be salvaged out of it; the grammar is anchored.
  assert.equal(builtAtOf(`${"x".repeat(200)}${PRODUCER_BUILT_AT}`), undefined);
  assert.equal(builtAtOf(`${PRODUCER_BUILT_AT}${"x".repeat(200)}`), undefined);
});

test("a build date that is not a string at all is refused", () => {
  for (const value of [20260815, 0, null, undefined, true, {}, [], [PRODUCER_BUILT_AT],
                       new Date("2026-08-15T09:22:08Z")]) {
    assert.equal(builtAtOf(value), undefined, `rendered a build date from ${typeof value}`);
  }
});

test("a malformed build date does not suppress the rest of that publisher's row", () => {
  // Fail closed on the date, not on the row: the other envelope facts are still true and rule 4
  // wants them on screen. Dropping the row would hide the stamp validity as well.
  const [row] = envelopeStripRows([envelope("lu-legilux", {
    freshness: { built_at: "tomorrow", corpus_commit: "e9c4df09", stamp_signature_valid: true },
  })]);
  assert.equal(row.publisher, "lu-legilux");
  assert.equal(row.builtAt, undefined);
  assert.equal(indexFreshnessLabel(row.builtAt), "index build date unavailable");
  assert.equal(row.corpusCommit, "e9c4df09");
  assert.equal(row.signatureValid, true);
  assert.equal(row.timelineSemantics, "publisher_applicability");
  assert.equal(row.codeCommit, "abc123");
});

test("a malformed response yields no strip rather than a fabricated one", () => {
  for (const raw of [null, undefined, {}, "envelope", [null], [{}], [{ envelope: [] }],
                     [{ envelope: { publisher: "" } }]]) {
    assert.deepEqual(envelopeStripRows(raw), [], `fabricated a row from ${JSON.stringify(raw)}`);
  }
});
