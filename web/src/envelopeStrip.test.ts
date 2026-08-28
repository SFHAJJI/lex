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

// Duplicate publisher identities. Search calls envelopeStripRows on the raw response, so arrival
// order is transport order, and a first-wins collapse would let the same two envelopes display
// two different corpus commits depending on which one the transport happened to put first.

const conflictPair = () => [
  envelope("lu-legilux"),
  envelope("lu-legilux", {
    freshness: {
      built_at: "2026-08-08T00:00:00Z",
      corpus_commit: "0000ffff",
      stamp_signature_valid: false,
    },
  }),
];

const UNESTABLISHED = {
  publisher: "lu-legilux",
  timelineSemantics: undefined,
  builtAt: undefined,
  signatureValid: undefined,
  corpusCommit: undefined,
  codeCommit: undefined,
  manifestSetId: undefined,
  contentDigest: undefined,
};

test("conflicting envelopes for one publisher state no identity rather than the first one", () => {
  const rows = envelopeStripRows(conflictPair());
  assert.equal(rows.length, 1);
  assert.deepEqual(rows[0], UNESTABLISHED, "picked one conflicting disclosure and rendered it");
  assert.equal(indexFreshnessLabel(rows[0].builtAt), "index build date unavailable");
});

test("arrival order cannot decide the displayed identity", () => {
  const [first, second] = conflictPair();
  assert.deepEqual(envelopeStripRows([first, second]), envelopeStripRows([second, first]));
  // Named explicitly as well, so a regression cannot pass by making both orders equally wrong.
  for (const order of [[first, second], [second, first]]) {
    const [row] = envelopeStripRows(order);
    assert.equal(row.builtAt, undefined, "arrival order produced a confident build date");
    assert.equal(row.corpusCommit, undefined, "arrival order produced a confident corpus commit");
    assert.equal(row.signatureValid, undefined, "arrival order produced a signature claim");
  }
});

test("a conflicted publisher is disclosed, never dropped from the strip", () => {
  // Dropping it would hide that the index answered at all, which reads as "not mounted" and is a
  // different, false statement. The reader has to be told the identity could not be established.
  const rows = envelopeStripRows(conflictPair());
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(indexFreshnessLabel(rows[0].builtAt), "index build date unavailable");
});

test("a disagreement on any single row field conflicts, not just the build date", () => {
  // Comparing a subset is the same defect in a subtler place: two entries agreeing on built_at and
  // disagreeing on corpus_commit would collapse into one confident row.
  const base = {
    timeline_semantics: "publisher_applicability",
    freshness: { built_at: "2026-08-15T09:01:06Z", corpus_commit: "e9c4df09",
                 stamp_signature_valid: true },
    artifact: { code_commit: "abc123", manifest_set_id: "m-1", content_digest: "d-1" },
  };
  const differing: Record<string, unknown>[] = [
    { ...base, timeline_semantics: "publisher_transaction" },
    { ...base, freshness: { ...base.freshness, built_at: "2026-08-08T00:00:00Z" } },
    { ...base, freshness: { ...base.freshness, stamp_signature_valid: false } },
    { ...base, freshness: { ...base.freshness, corpus_commit: "0000ffff" } },
    { ...base, artifact: { ...base.artifact, code_commit: "def456" } },
    { ...base, artifact: { ...base.artifact, manifest_set_id: "m-2" } },
    { ...base, artifact: { ...base.artifact, content_digest: "d-2" } },
  ];
  for (const over of differing) {
    const rows = envelopeStripRows([envelope("lu-legilux", base), envelope("lu-legilux", over)]);
    assert.equal(rows.length, 1);
    assert.deepEqual(rows[0], UNESTABLISHED, `collapsed a conflict: ${JSON.stringify(over)}`);
  }
});

test("a field absent on one entry and present on the other is a conflict, not a merge", () => {
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope("lu-legilux", { freshness: { built_at: "2026-08-15T09:01:06Z",
                                          stamp_signature_valid: true } }),
  ]);
  assert.deepEqual(rows[0], UNESTABLISHED, "kept a corpus commit only one envelope carried");
});

test("entries whose raw values differ but normalize alike are one disclosure", () => {
  // Equality is over what the row would state, not over the bytes that produced it. Both of these
  // fail closed to undefined on every field, so they say the same thing and collapsing states
  // nothing neither of them said.
  const rows = envelopeStripRows([
    envelope("lu-legilux", { timeline_semantics: 7, freshness: { built_at: "tomorrow",
      corpus_commit: "", stamp_signature_valid: "yes" }, artifact: { code_commit: null } }),
    envelope("lu-legilux", { timeline_semantics: null, freshness: { built_at: 20260815,
      corpus_commit: "   ", stamp_signature_valid: 1 }, artifact: { code_commit: "x".repeat(129) } }),
  ]);
  assert.equal(rows.length, 1, "treated two identical fail-closed disclosures as a conflict");
  assert.deepEqual(rows[0], UNESTABLISHED);
});

test("a third entry conflicting with two agreeing ones still fails the row closed", () => {
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope("lu-legilux"),
    envelope("lu-legilux", { freshness: { built_at: "2026-08-08T00:00:00Z" } }),
  ]);
  assert.equal(rows.length, 1);
  assert.deepEqual(rows[0], UNESTABLISHED, "an agreeing pair absorbed a later conflict");
});

test("one publisher's conflict does not disturb another publisher's row", () => {
  const rows = envelopeStripRows([
    ...conflictPair(),
    envelope("eu-eurlex", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                         corpus_commit: "aa11bb22", stamp_signature_valid: true } }),
  ]);
  // Ordinal sort survives the conflict handling, so the strip renders identically between runs.
  assert.deepEqual(rows.map((r) => r.publisher), ["eu-eurlex", "lu-legilux"]);
  assert.equal(rows[0].builtAt, "2026-08-08T00:00:00Z");
  assert.equal(rows[0].corpusCommit, "aa11bb22");
  assert.equal(rows[0].signatureValid, true);
  assert.deepEqual(rows[1], UNESTABLISHED);
});


// Publisher identity attacks. The publisher is the row KEY, so a validator that trims or case-
// folds it does not merely admit a bad value: it decides which rows share one disclosure. The
// strip used to validate it through `str`, which trims, while the population footer and the
// limitation list refused the padded form outright. See publisherIdentity.ts.

test("a padded publisher is refused, never trimmed into the real identity", () => {
  // Under a trimming validator the padded entry becomes a SECOND entry for lu-legilux, and
  // because its corpus commit differs it conflicts and blanks a row that no envelope disputed.
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope(" lu-legilux ", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                            corpus_commit: "0000ffff",
                                            stamp_signature_valid: false } }),
  ]);
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"],
    "the padded spelling became a row of its own, or displaced the real one");
  assert.equal(rows[0].builtAt, "2026-08-15T09:01:06Z",
    "a padded entry was trimmed into this publisher and conflicted its identity away");
  assert.equal(rows[0].corpusCommit, "e9c4df09");
  assert.equal(rows[0].signatureValid, true);
});

test("a case alias is refused, never folded into the lower-case identity", () => {
  // Producer registry identities are ordinal lower-case, so LU-Legilux is a spelling the
  // producer cannot mint. Folding it conflicts the real row away; admitting it as its own key
  // splits one publisher into two rows of the strip.
  const rows = envelopeStripRows([
    envelope("lu-legilux"),
    envelope("LU-Legilux", { freshness: { built_at: "2026-08-08T00:00:00Z",
                                          corpus_commit: "0000ffff",
                                          stamp_signature_valid: false } }),
  ]);
  assert.deepEqual(rows.map((r) => r.publisher), ["lu-legilux"]);
  assert.equal(rows[0].builtAt, "2026-08-15T09:01:06Z");
  assert.equal(rows[0].corpusCommit, "e9c4df09");
});

test("the strip refuses every publisher spelling the producer cannot mint", () => {
  // The strip previously bounded this field at 128, the length it uses for commit hashes. The
  // producer's own publisher bound is 64 (UiEffects.Identifier, MaximumShortLength), and "?" is
  // IndexReader.Collection's missing-stamp sentinel, not a publisher.
  for (const bad of [" lu-legilux ", "lu-legilux ", "LU-LEGILUX", "lu-legiluX", "?",
                     "lu legilux", "x".repeat(65), 7, null, {}]) {
    assert.deepEqual(envelopeStripRows([envelope(bad as string)]), [],
      `rendered a row for publisher ${JSON.stringify(bad)}`);
  }
  assert.equal(envelopeStripRows([envelope("x".repeat(64))]).length, 1,
    "the producer's own bound must remain renderable");
});

test("an identical duplicate still collapses into one confident row", () => {
  // The repair may not turn every repeat into an unavailable strip. Two entries saying the same
  // thing are one disclosure, and the reader keeps the identity both of them carried.
  const rows = envelopeStripRows([envelope("lu-legilux"), envelope("lu-legilux")]);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].builtAt, "2026-08-15T09:01:06Z");
  assert.equal(rows[0].corpusCommit, "e9c4df09");
  assert.equal(rows[0].signatureValid, true);
  assert.equal(rows[0].contentDigest, "d-1");
});
